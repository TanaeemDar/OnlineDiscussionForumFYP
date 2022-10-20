using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace OnlineDisscussionForum
{
    public class Program
    {
        public static void Main(string[] args)
        {
            BuildWebHost(args).Run();
        }

        public static IWebHost BuildWebHost(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
        //    .ConfigureAppConfiguration((builderContext, config) =>
        //{
        //    IHostingEnvironment env = builderContext.HostingEnvironment;
        //    config.AddJsonFile("storageSettings.json", optional: false, reloadOnChange: true);
        //})
                .UseStartup<Startup>()
                .Build();
    }
}
