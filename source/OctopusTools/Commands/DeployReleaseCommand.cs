﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using OctopusTools.Client;
using OctopusTools.Infrastructure;
using log4net;
using OctopusTools.Model;

namespace OctopusTools.Commands
{
    [Command("deploy-release", Description = "Deploys an existing release")]
    public class DeployReleaseCommand : ApiCommand
    {
        readonly IDeploymentWatcher deploymentWatcher;

        public DeployReleaseCommand(IOctopusSessionFactory session, ILog log, IDeploymentWatcher deploymentWatcher)
            : base(session, log)
        {
            this.deploymentWatcher = deploymentWatcher;

            DeployToEnvironmentNames = new List<string>();
            DeploymentStatusCheckSleepCycle = TimeSpan.FromSeconds(10);
            DeploymentTimeout = TimeSpan.FromMinutes(10);
        }

        public string ProjectName { get; set; }
        public IList<string> DeployToEnvironmentNames { get; set; }
        public string VersionNumber { get; set; }
        public bool Force { get; set; }
        public bool ForcePackageDownload { get; set; }
        public bool WaitForDeployment { get; set; }
        public TimeSpan DeploymentTimeout { get; set; }
        public TimeSpan DeploymentStatusCheckSleepCycle { get; set; }

        protected override void SetOptions(OptionSet options)
        {
            options.Add("deployfrom=", "Environment to pull version number from", v => DeployFromEnvironment = v);
            options.Add("project=", "Name of the project", v => ProjectName = v);
            options.Add("deployto=", "Environment to deploy to, e.g., Production", v => DeployToEnvironmentNames.Add(v));
            options.Add("releaseNumber=|version=", "Version number of the release to deploy.", v => VersionNumber = v);
            options.Add("force", "Whether to force redeployment of already installed packages (flag, default false).", v => Force = true);
            options.Add("forcepackagedownload", "Whether to force downloading of already installed packages (flag, default false).", v => ForcePackageDownload = true);
            options.Add("waitfordeployment", "Whether to wait synchronously for deployment to finish.", v => WaitForDeployment = true);
            options.Add("deploymenttimeout=", "[Optional] Specifies maximum time (timespan format) that deployment can take (default 00:10:00)", v => DeploymentTimeout = TimeSpan.Parse(v));
            options.Add("deploymentchecksleepcycle=", "[Optional] Specifies how much time (timespan format) should elapse between deployment status checks (default 00:00:10)", v => DeploymentStatusCheckSleepCycle = TimeSpan.Parse(v));
        }

        public string DeployFromEnvironment { get; set; }

        protected override void Execute()
        {
            if (string.IsNullOrWhiteSpace(ProjectName)) throw new CommandException("Please specify a project name using the parameter: --project=XYZ");
            if (DeployToEnvironmentNames.Count == 0) throw new CommandException("Please specify an environment using the parameter: --deployto=XYZ");
            if (string.IsNullOrWhiteSpace(VersionNumber) && string.IsNullOrWhiteSpace(DeployFromEnvironment)) throw new CommandException("Please specify a release version or deploy from environment using the parameter: --version=1.0.0.0 or --deployfromenvironment=dev");

            Log.Debug("Finding project: " + ProjectName);
            var project = Session.GetProject(ProjectName);

            Log.Debug("Finding environments...");
            var environments = Session.FindEnvironments(DeployToEnvironmentNames);

            Release release;
            if (!string.IsNullOrWhiteSpace(DeployFromEnvironment))
            {
                var envi=Session.FindEnvironments(new Collection<string>{DeployFromEnvironment}).First();
                var deployment = Session.GetMostRecentDeployments(project).FirstOrDefault(d => d.EnvironmentId == envi.Id);            
                 release = Session.List<Release>(project.Link("Releases")).First(r=>r.Id==deployment.ReleaseId);
            }
            else
            {
                Log.Debug("Finding release: " + VersionNumber);
                release = Session.GetRelease(project, VersionNumber);                
            }

            if (environments == null || environments.Count <= 0) return;
            var linksToDeploymentTasks = Session.GetDeployments(release, environments, Force, ForcePackageDownload, Log).ToList();

            if (WaitForDeployment)
            {
                deploymentWatcher.WaitForDeploymentsToFinish(Session, linksToDeploymentTasks, DeploymentTimeout, DeploymentStatusCheckSleepCycle);
            }
        }
    }
}
 