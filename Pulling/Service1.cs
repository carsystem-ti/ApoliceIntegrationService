using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace Pulling
{
    public partial class Service1 : ServiceBase
    {
        PullingService pullingService;

        public Service1()
        {
            InitializeComponent();

            pullingService = new PullingService();
        }

        protected override void OnStart(string[] args)
        {
            pullingService.Run();
        }

        public void StartDebug()
        {
            OnStart(null);
        }

        protected override void OnStop()
        {
            //pullingService.Stop();
        }
    }
}
