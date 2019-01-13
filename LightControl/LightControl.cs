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
        LightController controller;
        Scheduler scheduler;

        public LightControl()
        {
            InitializeComponent();
            Config cfg = new Config("../../config.yml");
            controller = new LightController(cfg.ApiKey, cfg.DispatchPeriod);
            //controller.Test();
            scheduler = new Scheduler(cfg.Schedules, cfg.Groups);
            Start();
            scheduler.ScheduleExpired += ScheduleTriggerHandler;
        }

        private async void Start()
        {
            await controller.Connect();
            scheduler.RunSchedule(scheduler.Schedules[0]);
        }

        private void ScheduleTriggerHandler(object src, SchedulerEventArgs e)
        {
            controller.SendCommand(e.Command);
        }
    }
}
