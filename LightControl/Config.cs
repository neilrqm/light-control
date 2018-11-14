using System;
using System.Collections.Generic;
using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

/* Configuration is defined in a YAML file:
 * 
 * ===============================
 *  
 *  schedules:
 *    default:                 # Create a schedule named "default"
 *      elements:                # list elements of the schedule
 *      - name: Morning            # Name this schedule element
 *        lights: bedroom          # which lighting group/alias this schedule affects
 *        days: [1, 2, 3, 4, 5]    # Effective on Mondays through Fridays (Sunday is 0, Saturday is 6)
 *        on: 7:00                 # Turn on at 7:00 AM (24-hour format)
 *        off: 7:15                # Turn off at 7:15 AM (24-hour format)
 *        ramp: 10                 # Ramp up brightness over 10 minutes
 *      - name: Evening
 *        lights: living room
 *        days: [0, 1, 2, 3, 4, 5, 6]
 *        on: 19:00
 *      - name: Night
 *        lights: all
 *        days: [0, 1, 2, 3, 4, 5, 6]
 *        off: 2:00
 *    away:
 *      inherit: default        # The "away" schedule is the same as "default"...
 *      elements:
 *      - name: Night             # ... except the "Night" schedule element
 *        off: 0:30               #     has its off time overwritten
 * 
 * ===============================
 * 
 * TODO: define aliases for lights and light sets
 *       colour temperature, brightness, sunset trigger, randomized on/off
 *       default colour temperature/brightness based on time/sunset
 *       reload mechanism for config file (monitor filesystem?  timer?  button on UI?)
 */

namespace LightControl
{
    class Config
    {
        private ConfigSchema config;

        /// <summary>
        /// Load a configuration YAML file.
        /// </summary>
        /// <param name="filename">Path to the YAML file to load.</param>
        internal Config(string filename)
        {
            IDeserializer deserializer = new DeserializerBuilder().WithNamingConvention(new CamelCaseNamingConvention()).Build();
            TextReader reader = File.OpenText(filename);
            try
            {
                config = deserializer.Deserialize<ConfigSchema>(reader);
            }
            catch (YamlDotNet.Core.SyntaxErrorException)
            {
                // How are we handling errors?
            }
        }

        /// <summary>
        /// A dictionary mapping schedule names to schedules.
        /// </summary>
        internal Dictionary<string, Schedule> Schedules => config.Schedules;
    }

    public class ScheduleElement
    {
        public string Name { get; set; }
        public string Lights { get; set; }
        public List<int> Days { get; set; }
        public DateTime? On { get; set; } = null;
        public DateTime? Off { get; set; } = null;
        public int Ramp { get; set; } = 0;
    }

    public class Schedule
    {
        public string Inherit { get; set; } = null;
        public List<ScheduleElement> Elements { get; set; } = new List<ScheduleElement>();
    }

    public class ConfigSchema
    {
        public Dictionary<string, Schedule> Schedules { get; set; }
    }
}
