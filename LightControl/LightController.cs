using Q42.HueApi;
using Q42.HueApi.Interfaces;
using Q42.HueApi.Models.Bridge;
using Q42.HueApi.Models.Groups;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace LightControl
{
    class LightController : IDisposable
    {
        private string apiKey;
        private int dispatchPeriod;
        private LocalHueClient client;
        private LocatedBridge bridge;
        private Queue<Command> dispatchQueue = new Queue<Command>();
        private Timer dispatchTimer;
        private List<Ramp> ramps = new List<Ramp>();

        internal static BridgeSimulator Simulator { get; private set; }
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// Initialize the light controller.
        /// </summary>
        /// <param name="apiKey">The key used to access the bridge</param>
        /// <param name="dispatchPeriod">The minimum gap between dispatches, in milliseconds.</param>
        internal LightController(string apiKey, int dispatchPeriod)
        {
            this.apiKey = apiKey;
            this.dispatchPeriod = dispatchPeriod;

            if (File.Exists("simulate.log"))    // this is a really dumb way to do this
            {
                Simulator = new BridgeSimulator("simulate.log");
            }

            ConnectionFailed += OnConnectionFailed;
            Connected += OnConnection;
            // can't connect if we don't have an API key defined.  TODO: get an API key
            if (string.IsNullOrEmpty(apiKey))
            {
                ConnectionFailed?.Invoke(this, new EventArgs());
                return;
            }

            // we dispatch messages based on a timer so that we don't send them too fast
            dispatchTimer = new Timer(dispatchPeriod);
            dispatchTimer.Elapsed += Dispatch;  // consider revising this to only do a full delay after sending a message
            dispatchTimer.Start();
        }

        /// <summary>
        /// Connect the controller to the light bridge.
        /// </summary>
        internal async Task<bool> Connect()
        {
            // Connect to the Philips Hue bridge.  If we ever change lights the Hue stuff can be abstracted out.
            if (Simulator != null)
            {
                Simulator.Log("Connection");
                return true;
            }
            IBridgeLocator locator = new HttpBridgeLocator();
            IEnumerable<LocatedBridge> bridges = await locator.LocateBridgesAsync(TimeSpan.FromSeconds(5));
            if (bridges == null || bridges.Count() == 0)
            {
                ConnectionFailed?.Invoke(this, new EventArgs());
                return false;
            }
            bridge = bridges.ElementAt(0);
            client = new LocalHueClient(bridge.IpAddress);
            // var appKey = await client.RegisterAsync("light-control", "fela");
            client?.Initialize(apiKey);
            bool connected = await client?.CheckConnection();
            if (client != null && connected)
            {
                Connected?.Invoke(this, new EventArgs());
            }
            else
            {
                ConnectionFailed?.Invoke(this, new EventArgs());
            }
            return connected;
        }

        /// <summary>
        /// If there is a command enqueued, dispatch it.  This function runs on a timer to ensure that commands are not
        /// sent too quickly.
        /// 
        /// If the command's Ramp property is non-zero, the command will be passed off to a new thread to execute the ramp.
        /// Otherwise, the command is converted to a light command and send to the lights.
        /// </summary>
        private void Dispatch(object sender, ElapsedEventArgs e)
        {
            if (dispatchQueue.Count > 0)    // this should be the only consumer, so no locking needed
            {
                Command c = dispatchQueue.Dequeue();
                if (c.Ramp != 0 && c.LightState == LightState.On)
                {
                    if (c.Brightness == null)
                    {
                        // TODO: error
                        return;
                    }
                    Ramp r = new Ramp(c, this);
                    r.RampDone += EndRamp;
                    ramps.Add(r);
                    r.StartRamp();
                }
                else if (Simulator != null)
                {
                    Simulator.Log(c);
                }
                else
                {
                    LightCommand command = new LightCommand();
                    // command.On is true to turn the light on, false to turn the light off, and null to leave the state unchanged.
                    command.On = c.LightState == LightState.On ? true : c.LightState == LightState.Off ? false : (bool?)null;
                    command.Brightness = (byte?)c.Brightness;
                    command.ColorTemperature = c.Colour;
                    client.SendCommandAsync(command, c.LightIds);
                }
            }
        }

        internal void SendCommand(Command c)
        {
            if (c.LightIds != null)
            {
                dispatchQueue.Enqueue(c);
            }
            else
            {
                throw new Exception("Command must specify a collection of light IDs");
            }
        }

        private void EndRamp(object sender, EventArgs e)
        {
            ramps.Remove((Ramp)sender);
        }

        private void OnConnectionFailed(object sender, EventArgs e) => dispatchTimer?.Stop();
        private void OnConnection(object sender, EventArgs e) => dispatchTimer?.Start();

        void IDisposable.Dispose()
        {
            ((IDisposable)dispatchTimer).Dispose();
        }

        internal async void Test()
        {
            await Connect();
            IEnumerable<Light> lights = await client.GetLightsAsync();
            Bridge x = await client.GetBridgeAsync();
            IReadOnlyCollection<Q42.HueApi.Models.Sensor> sensors = await client.GetSensorsAsync();
        }

        internal event EventHandler ConnectionFailed;
        internal event EventHandler Connected;
    }

    enum LightState
    {
        NoChange,
        Off,
        On,
    }

    // TODO: cancel ramp (e.g. if light switch is pressed)
    // TODO: break out starting brightness setting
    // TODO: ramp down (maybe)
    class Ramp
    {
        Command command;
        LightController controller;
        int remainingSteps;
        Timer rampTimer;
        int brightnessTarget;
        int rampDuration;
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        internal Ramp(Command c, LightController controller)
        {
            command = new Command()
            {
                Colour = c.Colour,
                LightIds = c.LightIds,
                LightState = LightState.On,
                Brightness = 1
            };
            rampDuration = c.Ramp;
            brightnessTarget = (int)c.Brightness;
            this.controller = controller;
        }
        internal void StartRamp()
        {
            remainingSteps = brightnessTarget - 1;   // assuming that we've already handled case where brightness is null
            int period = (rampDuration * 60 * 1000 - 2000) / remainingSteps;  // Not sure what the deal is with the 2 second offset, but it seems to be needed.
            // worst case is we'll step up brightness by 253 over the course of 1 minute, which works out to
            // well over 200 ms between steps.  As long as the dispatch period is shorter than that we'll be
            // fine, but if the dispatch period is more than 200 ms (or if the minimum ramp time or brightness
            // range changes) we'll need to step by more brightness less often
            rampTimer = new Timer(period);
            rampTimer.Elapsed += Step;
            if (LightController.Simulator == null)
            {
                command.LightState = LightState.On;
                command.Brightness = 1;
                controller.SendCommand(new Command(command));
            }
            else
            {
                LightController.Simulator.Log("Starting ramp.");
                LightController.Simulator.Log(command);
            }
            log.Debug(string.Format("Starting ramp with period {0}.", period));
            rampTimer.Start();
        }

        private void Step(object sender, ElapsedEventArgs e)
        {
            command.Brightness++;
            if (LightController.Simulator == null)
            {
                controller.SendCommand(new Command(command));
                log.Debug("Incrementing brightness.");
            }
            else
            {
                LightController.Simulator.Log("Incrementing brightness in ramp.");
            }
            remainingSteps--;
            if (remainingSteps == 0)
            {
                log.Debug("Stopping ramp.");
                rampTimer.Stop();
                rampTimer.Close();
                RampDone?.Invoke(this, new EventArgs());
            }
        }
        internal event EventHandler RampDone;
    }

    class Command
    {
        internal Command() { }
        internal Command(Command source)
        {
            Brightness = source.Brightness;
            Colour = source.Colour;
            LightIds = new List<string>(source.LightIds);
            LightState = source.LightState;
            Ramp = source.Ramp;
        }
        internal LightState LightState { get; set; }    // Valid values are Off, On, or NoChange
        internal int? Brightness { get; set; }  // Valid values are in the range 1-254, or null to leave unchanged
        internal int? Colour { get; set; }      // In mireks, valid from 500 (=2000K) down to 153 (=6500K), or null to leave unchanged
        internal List<string> LightIds { get; set; }
        internal int Ramp { get; set; } = 0;    // Brightness will ramp up from 1 to Brightness parameter over this many minutes (0 means no ramp)
    }

    class BridgeSimulator
    {
        private StreamWriter logFile;

        internal BridgeSimulator(string path)
        {
            logFile = new StreamWriter(path);
        }

        internal void Log(string message)
        {
            logFile.WriteLine(string.Format("{0} - {1}", DateTime.Now.ToString("yy-MM-dd HH:mm:ss.fff"), message));
            logFile.Flush();
        }

        internal void Log(Command c)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("yy-MM-dd HH:mm:ss.fff"));
            sb.Append(" - Command: '");
            sb.Append(string.Format("State: {0}, Bright: {1}, Colour: {2}, Ids: {3}, Ramp: {4}",
                c.LightState, c.Brightness, c.Colour, c.LightIds, c.Ramp));
            sb.Append("'");
            logFile.WriteLine(sb.ToString());
            logFile.Flush();
        }
    }
}
