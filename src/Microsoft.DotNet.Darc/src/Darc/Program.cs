using System.Collections.Generic;
using Microsoft.DotNet.Darc;

/*
 Placeholder class used for testing purposes during prototyping. Darc CLI will be implemented here.
*/

namespace Darc
{
    class Program
    {
        static void Main(string[] args)
        {
            DarcSettings settings = new DarcSettings
            {
                PersonalAccessToken = "token",
                GitType = GitRepoType.Vsts
            };

            DarcLib darc = new DarcLib(settings);
            IEnumerable<DependencyItem> dependenciesToUpdate = darc.RemoteAction.GetRequiredUpdatesAsync("https://dotnet.visualstudio.com/internal/_git/dotnet-arcade", "juanam/test").Result;
            string prLink = darc.RemoteAction.CreatePullRequestAsync(dependenciesToUpdate, "https://dotnet.visualstudio.com/internal/_git/dotnet-arcade", "juanam/test").Result;
            //string prLink = darc.RemoteAction.UpdatePullRequestAsync(dependenciesToUpdate, "https://github.com/jcagme/arcade/", "test", 7).Result;
        }
    }
}
