using Q42.HueApi;
using Q42.HueApi.Interfaces;
using Q42.HueApi.Models.Bridge;
using Q42.HueApi.Models.Groups;
using System;
using System.Collections.Generic;
using System.Linq;
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
        List<Ramp> ramps = new List<Ramp>();

        /// <summary>
        /// Initialize the light controller.
        /// </summary>
        /// <param name="apiKey">The key used to access the bridge</param>
        /// <param name="dispatchPeriod">The minimum gap between dispatches, in milliseconds.</param>
        internal LightController(string apiKey, int dispatchPeriod)
        {
            this.apiKey = apiKey;
            this.dispatchPeriod = dispatchPeriod;

            // we dispatch messages based on a timer so that we don't send them too fast
            dispatchTimer = new Timer(dispatchPeriod);
            dispatchTimer.Elapsed += Dispatch;  // consider revising this to only do a full delay after sending a message

            ConnectionFailed += OnConnectionFailed;
            Connected += OnConnection;
            // can't connect if we don't have an API key defined.  TODO: get an API key
            if (string.IsNullOrEmpty(apiKey))
            {
                ConnectionFailed?.Invoke(this, new EventArgs());
                return;
            }
        }

        /// <summary>
        /// Connect the controller to the light bridge.
        /// </summary>
        internal async void Connect()
        {
            // Connect to the Philips Hue bridge.  If we ever change lights the Hue stuff can be abstracted out.
            IBridgeLocator locator = new HttpBridgeLocator();
            IEnumerable<LocatedBridge> bridges = await locator.LocateBridgesAsync(TimeSpan.FromSeconds(5));
            if (bridges == null || bridges.Count() == 0)
            {
                ConnectionFailed?.Invoke(this, new EventArgs());
                return;
            }
            bridge = bridges.ElementAt(0);
            client = new LocalHueClient(bridge.IpAddress);
            client?.Initialize(apiKey);
            if (client != null && await client.CheckConnection())
            {
                Connected?.Invoke(this, new EventArgs());
            }
            else
            {
                ConnectionFailed?.Invoke(this, new EventArgs());
            }
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
                if (c.Ramp != 0)
                {
                    if (c.Brightness == null)
                    {
                        // TODO: error
                        return;
                    }
                    Ramp r = new Ramp(c, client);
                    r.RampDone += EndRamp;
                    r.StartRamp();
                    ramps.Add(r);
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
            dispatchQueue.Enqueue(c);
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
        LocalHueClient client;
        int remainingSteps;
        Timer rampTimer;

        internal Ramp(Command c, LocalHueClient client)
        {
            command = c;
            this.client = client;
        }
        internal void StartRamp()
        {
            remainingSteps = (int)command.Brightness - 1;   // assuming that we've already handled case where brightness is null
            int period = command.Ramp * 60 * 1000 / ((int)command.Brightness - 1);
            // worst case is we'll step up brightness by 253 over the course of 1 minute, which works out to
            // well over 200 ms between steps.  As long as the dispatch period is shorter than that we'll be
            // fine, but if the dispatch period is more than 200 ms (or if the minimum ramp time or brightness
            // range changes) we'll need to step by more brightness less often
            rampTimer = new Timer(period);
            rampTimer.Elapsed += Step;
            LightCommand initMessage = new LightCommand().TurnOn(); // right now I only want to ramp on, maybe ramp off later.
            initMessage.Brightness = 1;     // TODO: consider breaking out the initial brightness setting
            initMessage.ColorTemperature = command.Colour;
            client.SendCommandAsync(initMessage, command.LightIds);
        }
        private void Step(object sender, ElapsedEventArgs e)
        {
            LightCommand message = new LightCommand();
            message.BrightnessIncrement = 1;
            client.SendCommandAsync(message, command.LightIds);
            remainingSteps--;
            if (remainingSteps == 0)
            {
                rampTimer.Stop();
                rampTimer.Close();
                RampDone?.Invoke(this, new EventArgs());
            }
        }
        internal event EventHandler RampDone;
    }

    class Command
    {
        internal LightState LightState { get; set; }    // Valid values are Off, On, or NoChange
        internal int? Brightness { get; set; }  // Valid values are in the range 1-254, or null to leave unchanged
        internal int? Colour { get; set; }      // In mireks, valid from 500 (=2000K) down to 153 (=6500K), or null to leave unchanged
        internal List<string> LightIds { get; private set; } = new List<string>();
        internal int Ramp { get; set; } = 0;    // Brightness will ramp up from 1 to Brightness parameter over this many minutes (0 means no ramp)
    }
}
