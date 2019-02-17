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
                schedules[name] = new WeeklySchedule(s, lightingGroups);
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

        private async void TriggerEvent(int delay)
        {
            log.InfoFormat("Triggering next event in {0} seconds ({1}).", delay, DateTime.Now.AddSeconds(delay).ToLongTimeString());
            await Task.Delay(delay * 1000);
            // process events that are supposed to run at this time
            int seconds = TimeSinceStartOfWeek();
            while (activeSchedule.Peek() != null && activeSchedule.Peek().RunTime <= seconds)
            {
                WeeklyScheduleElement e = activeSchedule.Dequeue();
                log.InfoFormat("Triggering event on {0} - '{1}'.", e.Name, e.Command.ToString());
                ScheduleExpired?.Invoke(this, new SchedulerEventArgs(e.Command));
                seconds = TimeSinceStartOfWeek();
            }
            if (activeSchedule.Peek() == null)
            {
                // if there's nothing left in the schedule, wait for the week to roll over and re-generate the schedule.
                int x = 604800 - TimeSinceStartOfWeek();  // 604800 seconds in 1 week
                log.InfoFormat("End of week - regenerating schedule in {0} seconds.", x);
                await Task.Delay(x * 1000);
                activeSchedule.RegenerateWeeklySchedule();
                // might be a bug here with the time change but I don't expect it to come up.
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
            WeeklyScheduleElement next = activeSchedule.Peek();
            if (next.RunTime > currentTime)
            {
                return next.RunTime - currentTime;
            }
            DateTime now = DateTime.Now;
            DateTime startOfNextWeek = now - new TimeSpan((int)now.DayOfWeek, now.Hour, now.Minute, now.Second) + new TimeSpan(7, 0, 0, 0);
            return (int)(startOfNextWeek - now).TotalSeconds + next.RunTime;
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
        private Schedule schedule;
        private Dictionary<string, List<string>> lightingGroups;

        private Queue<WeeklyScheduleElement> elements;

        internal string Name { get; set; }

        internal WeeklySchedule(Schedule s, Dictionary<string, List<string>> lightingGroups)
        {
            schedule = s;
            elements = new Queue<WeeklyScheduleElement>();
            this.lightingGroups = lightingGroups;
            RegenerateWeeklySchedule();
        }

        internal WeeklySchedule(WeeklySchedule source)
        {
            Name = source.Name;
            elements = new Queue<WeeklyScheduleElement>();
            schedule = new Schedule(source.schedule);
            lightingGroups = new Dictionary<string, List<string>>(lightingGroups);
            foreach (WeeklyScheduleElement e in source.elements)
            {
                elements.Enqueue(new WeeklyScheduleElement(e));
            }
        }
        
        internal void Enqueue(WeeklyScheduleElement e) => elements.Enqueue(e);
        internal WeeklyScheduleElement Dequeue()
        {
            if (elements.Count != 0)
            {
                return elements.Dequeue();
            }
            return null;
        }
        internal WeeklyScheduleElement Peek()
        {
            if (elements.Count != 0)
            {
                return elements.Peek();
            }
            return null;
        }

        private int SecondsSinceStartOfWeek(int dayOfWeek, DateTime dt)
        {
            int seconds = dayOfWeek * 24 * 60 * 60;
            seconds += dt.Hour * 60 * 60;
            seconds += dt.Minute * 60;
            seconds += dt.Second;
            return seconds;
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

        private List<WeeklyScheduleElement> ConfigScheduleElementToWeeklyScheduleElementList(ScheduleElement e)
        {
            List<WeeklyScheduleElement> elementList = new List<WeeklyScheduleElement>();
            if (e.Days == null)
            {
                // error
            }
            foreach (int day in e.Days)
            {
                if (e.On != null)
                {
                    elementList.Add(CreateWeeklyScheduleElement(LightState.On, e, day));
                }
                if (e.Off != null)
                {
                    elementList.Add(CreateWeeklyScheduleElement(LightState.Off, e, day));
                }
            }
            return elementList;
        }

        internal void RegenerateWeeklySchedule()
        {
            elements.Clear();
            List<WeeklyScheduleElement> elementList = new List<WeeklyScheduleElement>();
            foreach (ScheduleElement e in schedule.Elements)
            {
                elementList.AddRange(ConfigScheduleElementToWeeklyScheduleElementList(e));
            }
            elementList.Sort();
            if (elementList[elementList.Count - 1].RunTime < TimeSinceStartOfWeek())
            {
                // if there aren't any more events for this week, just add a full week's worth of elements.
                foreach (WeeklyScheduleElement element in elementList)
                {
                    elements.Enqueue(element);
                }
            }
            else
            {
                // if there are more events for this week, just add those elements.
                foreach (WeeklyScheduleElement element in elementList)
                {
                    if (element.RunTime > TimeSinceStartOfWeek())
                    {
                        elements.Enqueue(element);
                    }
                }
            }
        }

        private int TimeSinceStartOfWeek()
        {
            DateTime now = DateTime.Now;
            DateTime start = now - new TimeSpan((int)now.DayOfWeek, now.Hour, now.Minute, now.Second);
            return (int)(DateTime.Now - start).TotalSeconds;
        }
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
