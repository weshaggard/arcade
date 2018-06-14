using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc
{
    public class VstsClient : IGitRepo
    {
        private const string VstsApiVersion = "5.0-preview.1";
        private const string DarcBranchName = "darc";
        private const string VersionPullRequestTitle = "[Darc-Update] global.json, version.props and version.details.xml";
        private const string VersionPullRequestDescription = "Darc is trying to update these files to the latest versions found in the Product Dependency Store";
        private readonly string personalAccessToken;

        private string VstsApiUri { get; set; }

        private string VstsAccountName { get; set; }

        private string VstsProjectName { get; set; }

        public VstsClient(string accessToken)
        {
            personalAccessToken = accessToken;
        }

        public async Task<string> GetFileContentsAsync(string filePath, string repoUri, string branch)
        {
            Console.WriteLine($"Getting the contents of file '{filePath}' from repo '{repoUri}' in branch '{branch}'...");
            string repoName = SetApiUriAndGetRepoName(repoUri);
            HttpResponseMessage response = await this.ExecuteGitCommand(HttpMethod.Get, $"git/repositories/{repoName}/items?path={filePath}&versionDescriptor[version]={branch}&includeContent=true");

            Console.WriteLine($"Getting the contents of file '{filePath}' from repo '{repoUri}' in branch '{branch}' succeeded!");

            dynamic responseContent = JsonConvert.DeserializeObject<dynamic>(await response.Content.ReadAsStringAsync());

            return responseContent.content;
        }

        public async Task CreateDarcBranchAsync(string repoUri, string branch)
        {
            Console.WriteLine($"Verifying if '{DarcBranchName}-{branch}' branch exist in repo '{repoUri}'. If not, we'll create it...");

            string repoName = SetApiUriAndGetRepoName(repoUri);
            string body;
            string operation;

            List<VstsRef> vstsRefs = new List<VstsRef>();
            VstsRef vstsRef;
            HttpResponseMessage response = null;

            string latestSha = await GetLastCommitShaAsync(repoName, branch);

            JsonSerializerSettings serializerSettings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            };

            response = await this.ExecuteGitCommand(HttpMethod.Get, $"git/repositories/{repoName}/refs/heads/{DarcBranchName}-{branch}");
            dynamic responseContent = JsonConvert.DeserializeObject<dynamic>(await response.Content.ReadAsStringAsync());

            // VSTS doesn't fail with a 404 if a branch does not exist, it just returns an empty response object...
            if (responseContent.count == 0)
            {
                Console.WriteLine($"'{DarcBranchName}' branch doesn't exist. Creating it...");
                vstsRef = new VstsRef($"refs/heads/{DarcBranchName}-{branch}", latestSha);
                vstsRefs.Add(vstsRef);
                operation = "created in";
            }
            else
            {
                Console.WriteLine($"Branch '{DarcBranchName}-{branch}' exists, making sure it is in sync with '{branch}'...");
                string oldSha = await GetLastCommitShaAsync(repoName, $"{DarcBranchName}-{branch}");
                vstsRef = new VstsRef($"refs/heads/{DarcBranchName}-{branch}", latestSha, oldSha);
                vstsRefs.Add(vstsRef);
                operation = "in sync with";
            }

            body = JsonConvert.SerializeObject(vstsRefs, serializerSettings);
            await this.ExecuteGitCommand(HttpMethod.Post, $"git/repositories/{repoName}/refs", body);

            Console.WriteLine($"Branch '{DarcBranchName}-{branch}' {operation} repo '{repoUri}'!");

            return;
        }

        public async Task PushFilesAsync(Dictionary<string, GitCommit> filesToCommit, string repoUri, string pullRequestBaseBranch)
        {
            List<VstsChange> changes = new List<VstsChange>();
            string repoName = SetApiUriAndGetRepoName(repoUri);

            foreach (string filePath in filesToCommit.Keys)
            {
                string content = this.GetDecodedContent(filesToCommit[filePath].Content);
                string blobSha = await CheckIfFileExistsAsync(repoUri, filePath, pullRequestBaseBranch);

                VstsChange change = new VstsChange(filePath, content);

                if (!string.IsNullOrEmpty(blobSha))
                {
                    change.ChangeType = VstsChangeType.Edit;
                }

                changes.Add(change);
            }

            VstsCommit commit = new VstsCommit(changes, "Dependency files update");

            string latestSha = await GetLastCommitShaAsync(repoName, pullRequestBaseBranch);
            VstsRefUpdate refUpdate = new VstsRefUpdate($"refs/heads/{pullRequestBaseBranch}", latestSha);

            VstsPush vstsPush = new VstsPush(refUpdate, commit);

            JsonSerializerSettings serializerSettings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                NullValueHandling = NullValueHandling.Ignore
            };

            string body = JsonConvert.SerializeObject(vstsPush, serializerSettings);

            await this.ExecuteGitCommand(HttpMethod.Post, $"git/repositories/{repoName}/pushes", body, "5.0-preview.2");
        }

        public Task<string> GetFileContentAsync(string ownerAndRepo, string path)
        {
            throw new NotImplementedException();
        }

        public Task<string> CheckForOpenedPullRequestsAsync(string repoUri, string darcBranch)
        {
            throw new NotImplementedException();
        }

        public Task<string> CreatePullRequestAsync(string repoUri, string mergeWithBranch, string sourceBranch, string title = null, string description = null)
        {
            throw new NotImplementedException();
        }

        public Task<string> UpdatePullRequestAsync(string repoUri, string mergeWithBranch, string sourceBranch, int pullRequestId, string title = null, string description = null)
        {
            throw new NotImplementedException();
        }

        public Task<Dictionary<string, GitCommit>> GetCommitsForPathAsync(string repoUri, string sha, string branch, string path = "eng")
        {
            throw new NotImplementedException();
        }

        public Task GetCommitMapForPathAsync(string repoUri, string sha, string branch, Dictionary<string, GitCommit> commits, string path = "eng")
        {
            throw new NotImplementedException();
        }

        public async Task<string> GetLastCommitShaAsync(string repo, string branch)
        {
            HttpResponseMessage response = await this.ExecuteGitCommand(HttpMethod.Get, $"git/repositories/{repo}/commits?branch={branch}");
            dynamic content = JsonConvert.DeserializeObject<dynamic>(await response.Content.ReadAsStringAsync());
            List<dynamic> values = JsonConvert.DeserializeObject<List<dynamic>>(Convert.ToString(content.value));

            if (!values.Any())
            {
                throw new Exception($"No commits found in branch '{branch}' of '{repo}'");
            }

            return values[0].commitId;
        }

        public HttpClient CreateHttpClient(string versionOverride = null)
        {
            HttpClient client = new HttpClient
            {
                BaseAddress = new Uri(VstsApiUri)
            };
            client.DefaultRequestHeaders.Add("Accept", $"application/json;api-version={versionOverride ?? VstsApiVersion}");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(string.Format("{0}:{1}", "", personalAccessToken))));
            return client;
        }

        public async Task<string> CheckIfFileExistsAsync(string repoUri, string filePath, string branch)
        {
            string repoName = SetApiUriAndGetRepoName(repoUri);
            HttpResponseMessage response;

            try
            {
                response = await this.ExecuteGitCommand(HttpMethod.Get, $"git/repositories/{repoName}/items?path={filePath}&versionDescriptor[version]={branch}");
            }
            catch (HttpRequestException exc)
            {
                if (exc.Message.Contains(((int)HttpStatusCode.NotFound).ToString()))
                {
                    return null;
                }

                throw exc;
            }

            dynamic content = JsonConvert.DeserializeObject<dynamic>(await response.Content.ReadAsStringAsync());

            return content.objectId;
        }

        private string SetApiUriAndGetRepoName(string repoUri)
        {
            Uri uri = new Uri(repoUri);
            string hostName = uri.Host;
            string accountName;
            string projectName;
            string repoName;

            Match hostNameMatch = Regex.Match(hostName, @"^(?<accountname>[a-z0-9]+)\.*");

            if (hostNameMatch.Success)
            {
                accountName = hostNameMatch.Groups["accountname"].Value;
            }
            else
            {
                throw new ArgumentException($"Repository URI host name '{hostName}' should be of the form <account name>.visualstudio.com i.e. https://<accountname>.visualstudio.com");
            }

            string absolutePath = uri.AbsolutePath;
            Match projectAndRepoMatch = Regex.Match(absolutePath, @"[\/DefaultCollection]*\/(?<projectname>.+)\/_git\/(?<reponame>.+)");

            if (projectAndRepoMatch.Success)
            {
                projectName = projectAndRepoMatch.Groups["projectname"].Value;
                repoName = projectAndRepoMatch.Groups["reponame"].Value;
            }
            else
            {
                throw new ArgumentException($"Repository URI host name '{absolutePath}' should have a project and repo name. i.e. /DefaultCollection/<projectname>/_git/<reponame>");
            }

            // TODO: If all requestUris turn out including /git, add it here and remove the segment from elsewhere
            VstsApiUri = $"https://{accountName}.visualstudio.com/{projectName}/_apis/";

            return repoName;
        }
    }
}
