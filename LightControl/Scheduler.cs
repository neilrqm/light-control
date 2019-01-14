using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LightControl
{
    delegate void ScheduleExpired(object src, SchedulerEventArgs e);

    /// <summary>
    /// Translate a set of schedules defined in the config file to a sequence of events mapping commands to runtimes.
    /// </summary>
    class Scheduler
    {
        internal Dictionary<string, WeeklySchedule> schedules;
        private WeeklySchedule activeSchedule;
        private Dictionary<string, List<string>> lightingGroups;
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        internal event ScheduleExpired ScheduleExpired;

        internal Scheduler(Dictionary<string, Schedule> configSchedules, Dictionary<string, List<string>> lightingGroups)
        {
            this.lightingGroups = lightingGroups;
            UpdateConfig(configSchedules);
            log.Info("Loaded schedules.");
        }

        internal void UpdateConfig(Dictionary<string, Schedule> configSchedules)
        {
            // for each config schedule:
            //   copy from inherited config schedules
            //   create a new weekly schedule
            //   for each config schedule element:
            //     for each day in the config schedule element, add a new weekly schedule element
            schedules = new Dictionary<string, WeeklySchedule>();
            foreach (string name in configSchedules.Keys)
            {
                Schedule s;
                if (string.IsNullOrEmpty(configSchedules[name].Inherit))
                {
                    s = configSchedules[name];
                }
                else
                {
                    s = new Schedule(configSchedules[configSchedules[name].Inherit]);
                    foreach (ScheduleElement e in configSchedules[name].Elements)
                    {
                        s.AddElement(e);
                    }
                }
                schedules[name] = new WeeklySchedule();
                foreach (ScheduleElement e in s.Elements)
                {
                    schedules[name].Elements.AddRange(ConfigScheduleElementToWeeklyScheduleElement(e));
                }
                schedules[name].Elements.Sort();
            }
        }

        internal void RunSchedule(string scheduleName)
        {
            if (!schedules.ContainsKey(scheduleName))
            {
                // error
                log.ErrorFormat("Couldn't find schedule {0}.", scheduleName);
                return;
            }
            log.Info(string.Format("Starting schedule '{0}'.", scheduleName));
            activeSchedule = schedules[scheduleName];
            TriggerEvent(SecondsUntilNextRun());
        }

        private List<WeeklyScheduleElement> ConfigScheduleElementToWeeklyScheduleElement(ScheduleElement e)
        {
            List<WeeklyScheduleElement> elements = new List<WeeklyScheduleElement>();
            if (e.Days ==null)
            {
                // error
            }
            foreach (int day in e.Days)
            {
                if (e.On != null)
                {
                    elements.Add(CreateWeeklyScheduleElement(LightState.On, e, day));
                }
                if (e.Off != null)
                {
                    elements.Add(CreateWeeklyScheduleElement(LightState.Off, e, day));
                }
            }
            return elements;
        }

        private WeeklyScheduleElement CreateWeeklyScheduleElement(LightState state, ScheduleElement e, int day)
        {
            Command cmd = new Command()
            {
                LightState = state,
                Ramp = e.Ramp,
                LightIds = new List<string>(lightingGroups[e.Lights]),
                Brightness = e.Brightness,
                Colour = e.Colour
            };
            if (cmd.Ramp != 0)
            {
                cmd.Brightness = 254;
            }
            WeeklyScheduleElement element = new WeeklyScheduleElement()
            {
                Command = cmd,
                Name = e.Name,
                RunTime = SecondsSinceStartOfWeek(day, (DateTime)(state == LightState.On ? e.On : e.Off))
            };
            return element;
        }

        private int SecondsSinceStartOfWeek(int dayOfWeek, DateTime dt)
        {
            int seconds = dayOfWeek * 24 * 60 * 60;
            seconds += dt.Hour * 60 * 60;
            seconds += dt.Minute * 60;
            seconds += dt.Second;
            return seconds;
        }

        private async void TriggerEvent(int delay)
        {
            log.InfoFormat("Triggering next event in {0} seconds.", delay);
            await Task.Delay(delay * 1000);
            // process events that are supposed to run at this time
            // do we need to mark the processed events so that SecondsUntilNextRun doesn't find them?  I don't think so but maybe.
            int seconds = TimeSinceStartOfWeek();
            foreach (WeeklyScheduleElement e in activeSchedule.Elements)
            {
                if (e.RunTime == seconds)
                {
                    log.InfoFormat("Triggering event on {0} - '{1}'.", e.Name, e.Command);
                    ScheduleExpired?.Invoke(this, new SchedulerEventArgs(e.Command));
                }
                else if (e.RunTime > seconds)
                {
                    break;
                }
            }
            TriggerEvent(SecondsUntilNextRun());
        }

        internal List<string> Schedules
        {
            get
            {
                return new List<string>(schedules.Keys);
            }
        }

        private int SecondsUntilNextRun()
        {
            int currentTime = TimeSinceStartOfWeek();
            foreach (WeeklyScheduleElement e in activeSchedule.Elements)
            {
                if (e.RunTime > currentTime)
                {
                    return e.RunTime - currentTime;
                }
            }
            DateTime now = DateTime.Now;
            DateTime startOfNextWeek = now - new TimeSpan((int)now.DayOfWeek, now.Hour, now.Minute, now.Second) + new TimeSpan(7, 0, 0, 0);
            return (int)(startOfNextWeek - now).TotalSeconds;
        }

        private int TimeSinceStartOfWeek()
        {
            DateTime now = DateTime.Now;
            DateTime start = now - new TimeSpan((int)now.DayOfWeek, now.Hour, now.Minute, now.Second);
            return (int)(DateTime.Now - start).TotalSeconds;
        }
    }

    class WeeklySchedule
    {
        internal WeeklySchedule() { }
        internal WeeklySchedule(WeeklySchedule source)
        {
            Name = source.Name;
            Elements = new List<WeeklyScheduleElement>();
            foreach (WeeklyScheduleElement e in source.Elements)
            {
                Elements.Add(new WeeklyScheduleElement(e));
            }
        }
        internal string Name { get; set; }
        internal List<WeeklyScheduleElement> Elements { get; set; } = new List<WeeklyScheduleElement>();
    }

    class WeeklyScheduleElement : IComparable
    {
        internal int RunTime { get; set; }  // Number of seconds after the start of the week when this element should run.
        internal string Name { get; set; }
        internal Command Command { get; set; }

        internal WeeklyScheduleElement() { }
        internal WeeklyScheduleElement(WeeklyScheduleElement source)
        {
            RunTime = source.RunTime;
            Name = source.Name;
            Command = new Command(source.Command);
        }

        public int CompareTo(object obj)
        {
            if (!(obj is WeeklyScheduleElement))
            {
                throw new ArgumentException(string.Format("Cannot compare WeeklyScheduledElement to {0}.", obj.GetType()));
            }
            WeeklyScheduleElement other = (WeeklyScheduleElement)obj;
            if (RunTime > other.RunTime) return 1;
            if (RunTime < other.RunTime) return -1;
            return 0;
        }
    }

    internal class SchedulerEventArgs : EventArgs
    {
        internal Command Command { get; set; }
        internal SchedulerEventArgs(Command command)
        {
            Command = command;
        }
    }
}
