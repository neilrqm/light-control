using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LightControl
{
    public partial class LightControl : Form
    {
        public LightControl()
        {
            InitializeComponent();
            Start();
        }

        private async void Start()
        {
            Config cfg = new Config("../../config.yml");
            //LightController lc = new LightController(cfg.ApiKey, cfg.DispatchPeriod);
            //await lc.Connect();
            //lc.Test();
            Scheduler scheduler = new Scheduler(cfg.Schedules, cfg.Groups);
            scheduler.RunSchedule(scheduler.Schedules[0]);
        }
    }
}
