﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LightControl
{
    /// <summary>
    /// Translate a set of schedules defined in the config file to a sequence of events mapping commands to runtimes.
    /// </summary>
    class Scheduler
    {
        internal Dictionary<string, WeeklySchedule> schedules;
        WeeklySchedule activeSchedule;
        Dictionary<string, List<string>> lightingGroups;

        internal Scheduler(Dictionary<string, Schedule> configSchedules, Dictionary<string, List<string>> lightingGroups)
        {
            this.lightingGroups = lightingGroups;
            UpdateConfig(configSchedules);
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
                return;
            }
            activeSchedule = schedules[scheduleName];
            TriggerEvent(SecondsUntilNextRun());
        }

        private List<WeeklyScheduleElement> ConfigScheduleElementToWeeklyScheduleElement(ScheduleElement e)
        {
            List<WeeklyScheduleElement> elements = new List<WeeklyScheduleElement>();
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
            Command cmd = new Command();
            cmd.LightState = state;
            cmd.Ramp = e.Ramp;
            cmd.LightIds = new List<string>(lightingGroups[e.Lights]);
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
            await Task.Delay(delay * 1000);
            // process events that are supposed to run at this time
            // do we need to mark the processed events so that SecondsUntilNextRun doesn't find them?  I don't think so but maybe.
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
}
