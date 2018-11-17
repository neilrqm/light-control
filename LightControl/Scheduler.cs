using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LightControl
{
    class Scheduler
    {
        Dictionary<string, WeeklySchedule> schedules;
        WeeklySchedule activeSchedule;

        internal Scheduler(Dictionary<string, Schedule> configSchedules)
        {
            UpdateConfig(configSchedules);
        }

        internal void UpdateConfig(Dictionary<string, Schedule> configSchedules)
        {
            // for each schedule:
            //   copy from inherited schedules
            //   create a new weekly schedule
            //   for each schedule element:
            //     for each day in the schedule element, add a new weekly schedule element
        }

        internal void RunSchedule(string scheduleName)
        {
            if (!schedules.ContainsKey(scheduleName))
            {
                // error
                return;
            }
            TriggerEvent(SecondsUntilNextRun());
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
        internal List<WeeklyScheduleElement> Elements { get; set; }
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
