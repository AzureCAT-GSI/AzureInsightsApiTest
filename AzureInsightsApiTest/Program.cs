using Microsoft.Azure;
using Microsoft.Azure.Insights;
using Microsoft.Azure.Insights.Models;
using Microsoft.Azure.Management.Resources;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.Rest;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace AzureInsightsApiTest
{
    class Program
    {
        private const string ClientId = "";
        private const string ClientSecret = "";
        private const string ResourceGroupName = "";
        private const string SubscriptionId = "";
        private const string TenantId = "";

        private static readonly InsightsClient insightsClient;
        private static readonly ResourceManagementClient resourceManagementClient;
        private static readonly string[] resourceTypes = new[] { "Microsoft.Compute/virtualMachines" };

        static Program()
        {
            var accessToken = GetAccessToken();
            var tokenCredentials = new TokenCredentials(accessToken);
            var tokenCloudCredentials = new TokenCloudCredentials(SubscriptionId, accessToken);

            resourceManagementClient = new ResourceManagementClient(tokenCredentials) { SubscriptionId = SubscriptionId };
            insightsClient = new InsightsClient(tokenCloudCredentials);
        }

        static void Main(string[] args)
        {
            MonitorResources(GetMetricDefinitions());
        }

        public static Dictionary<string, List<MetricDefinition>> GetMetricDefinitions()
        {
            var resourceGroup = resourceManagementClient.ResourceGroups.Get(ResourceGroupName);
            var metricDefinitions = new Dictionary<string, List<MetricDefinition>>();

            foreach (var resource in resourceManagementClient.Resources.List()
                .Where(r => (resourceTypes.Contains(r.Type)) && (r.Id.StartsWith(resourceGroup.Id))))
            {
                var metricDefinitionsResponse = insightsClient.MetricDefinitionOperations.GetMetricDefinitions(resource.Id, "");

                metricDefinitions.Add(resource.Id, new List<MetricDefinition>());

                Console.WriteLine("Resource Id");
                Console.WriteLine(new string('=', 100));
                Console.WriteLine(resource.Id);
                Console.WriteLine();

                Console.WriteLine("Available Metrics");
                Console.WriteLine(new string('=', 100));

                foreach (var metric in metricDefinitionsResponse.MetricDefinitionCollection.Value)
                {
                    Console.WriteLine(metric.Name.Value);
                    metricDefinitions[resource.Id].Add(metric);
                }

                Console.WriteLine();
            }

            return metricDefinitions;
        }

        public static void MonitorResources(Dictionary<string, List<MetricDefinition>> metricDefinitions)
        {
            const string dateTimeFormat = "yyy-MM-ddTHH:mmZ";

            while (true)
            {
                foreach (var resourceId in metricDefinitions.Keys)
                {
                    var resourceMetricDefinitions = metricDefinitions[resourceId];

                    Console.WriteLine("Resource Id");
                    Console.WriteLine(new string('=', 100));
                    Console.WriteLine(resourceId);
                    Console.WriteLine();

                    Console.WriteLine("Available Metrics");
                    Console.WriteLine(new string('=', 100));

                    var startTime = DateTimeOffset.UtcNow.AddMinutes(-5).ToString(dateTimeFormat);
                    var endTime = DateTimeOffset.UtcNow.ToString(dateTimeFormat);
                    var filter = $"startTime eq {startTime} and endTime eq {endTime} and timeGrain eq duration'PT1M'";
                    var getMetricsResponse = insightsClient.MetricOperations.GetMetrics(resourceId, filter);
                    var metrics = getMetricsResponse.MetricCollection.Value.Where(m => m.MetricValues.Any());
                    var maxMetricNameLength = metrics.Max(m => m.Name.Value.Length);

                    foreach (var metric in getMetricsResponse.MetricCollection.Value.Where(m => m.MetricValues.Any()))
                    {
                        var lastMetricValue = metric.MetricValues.OrderByDescending(mv => mv.Timestamp).First();

                        Console.WriteLine(metric.Name.Value + 
                            new string(' ', (maxMetricNameLength - metric.Name.Value.Length)) +
                            $" : [{lastMetricValue.Timestamp.ToString("t")} UTC] [Avg {lastMetricValue.Average} {metric.Unit}]");
                    }

                    Console.WriteLine();
                }

                Thread.Sleep(60000);
            }
        }

        public static string GetAccessToken()
        {
            var authenticationContext = new AuthenticationContext($"https://login.windows.net/{TenantId}");
            var credential = new ClientCredential(clientId: ClientId, clientSecret: ClientSecret);

            var result =
                authenticationContext.AcquireTokenAsync("https://management.core.windows.net/", credential).Result;

            if (result == null)
                throw new InvalidOperationException("Failed to obtain the JWT token.");

            return result.AccessToken;
        }
    }
}
