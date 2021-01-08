using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace Jira2Azure
{
    class Program
    {
        static string JIRAIssueQuery = "http://jiraserver:8080/rest/api/latest/search?jql={0}&fields=*all";
        static string TFSBugUrl = "https://dev.azure.com/Test/SampleProject/_apis/wit/workitems/$Bug?api-version=6.0";
        static string TFSAttachmentUrl = "https://dev.azure.com/Test/SampleProject/_apis/wit/attachments?fileName={0}&api-version=6.0";

        static async Task<dynamic> JIRAGetIssues(string jql)
        {
            using (var client = new HttpClient())
            {
                //your Jira login credential
                client.DefaultRequestHeaders.Add("Authorization", "Basic XXXXXXXXXX");
                var msg = await client.GetStringAsync(string.Format(JIRAIssueQuery, jql));
                dynamic issues = JsonConvert.DeserializeObject(msg);
                return issues;
            }
        }

        static async Task JIRA2TFS(string jql)
        {
            var issues = await JIRAGetIssues(jql);
            foreach (var issue in issues.issues)
            {
                var tfs = ConvertJIRA2TFS(issue);
                await TFSCreateIssue(tfs);
            }
        }

        static async Task<dynamic> TFSUploadFile(string file, byte[] byteData)
        {
            file = Uri.EscapeUriString(file);
            using (var client = new HttpClient())
            {
                //your Azure login credential
                client.DefaultRequestHeaders.Add("Authorization", "Basic XXXXXXXXX");
                using (var content = new ByteArrayContent(byteData))
                {
                    content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                    var msg = await client.PostAsync(string.Format(TFSAttachmentUrl, file), content);
                    var responseBody = msg.Content.ReadAsStringAsync().Result;
                    return JsonConvert.DeserializeObject(responseBody);
                }
            }
        }
        static byte[] JIRADownloadFile(dynamic url)
        {
            using (var client = new HttpClient())
            {
                //your Jira login credential
                client.DefaultRequestHeaders.Add("Authorization", "Basic XXXXXXXX");
                var content = client.GetByteArrayAsync((string)url).Result;
                return content;
            }
        }
        static string ConvertJIRA2TFS(dynamic issue)
        {
            //Additional JIRA link for documentation
            string json = $"[{{\"op\": \"add\",\"path\": \"/relations/-\",\"value\": {{\"rel\": \"Hyperlink\",\"url\": \"{issue.self}\", \"attributes\": {{\"comment\": \"{issue.key}\"}}}}}},";
            if (issue.fields.attachment != null)
            {
                foreach (var attach in issue.fields.attachment)
                {
                    var content = JIRADownloadFile(attach.content);
                    var attchUrl = TFSUploadFile((string)attach.filename, content).Result;
                    var attachedJson = $"{{\"rel\":\"AttachedFile\",\"url\":\"{(string)attchUrl.url}\",\"attributes\":{{\"resourceSize\":{content.Length},\"name\":\"{(string)attach.filename}\"}}}}";
                    json += string.Format("{{\"op\":\"add\",\"path\":\"{0}\",\"from\": null,\"value\":{1}}},", "/relations/-", attachedJson);
                }
            }
            string operation = "{{\"op\":\"add\",\"path\":\"{0}\",\"from\": null,\"value\":\"{1}\"}},";
            //######### more field mapping
            var fieldMapping = new Dictionary<string, dynamic>()
            {
                { "/fields/System.Title",issue.fields.summary },
                { "/fields/Microsoft.VSTS.TCM.ReproSteps", issue.fields.description},
                { "/fields/Microsoft.VSTS.Common.Priority", issue.fields.priority.id},
            };

            foreach (var field in fieldMapping)
            {
                if (field.Value != null)
                {
                    var str = ((string)field.Value).Replace("\"", "\\\"");
                    json += string.Format(operation, field.Key, str);
                }
            }

            json = json.Replace("\r\n", "<br/>");
            json = json.TrimEnd(',');
            json += "]";
            return json;
        }

        static async Task TFSCreateIssue(string json)
        {
            using (var client = new HttpClient())
            {
                //your Azure login credential
                client.DefaultRequestHeaders.Add("Authorization", "Basic XXXXXXXXXX");
                var content = new StringContent(json, Encoding.UTF8, "application/json-patch+json");
                var msg = await client.PostAsync(TFSBugUrl, content);
            }
        }

        static async Task Main(string[] args)
        {
            await JIRA2TFS("project = TAP");
        }
    }
}

