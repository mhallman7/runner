﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GitHub.DistributedTask.Pipelines;
using GitHub.DistributedTask.WebApi;
using GitHub.Services.Common;
using GitHub.Services.OAuth;
using GitHub.Services.WebApi;
using Newtonsoft.Json;
using Sdk.RSWebApi.Contracts;
using Sdk.WebApi.WebApi;

namespace GitHub.Actions.RunService.WebApi
{
    public class RunServiceHttpClient : RawHttpClientBase
    {
        private static readonly JsonSerializerSettings s_serializerSettings;

        static RunServiceHttpClient()
        {
            s_serializerSettings = new VssJsonMediaTypeFormatter().SerializerSettings;
            s_serializerSettings.DateParseHandling = DateParseHandling.None;
            s_serializerSettings.FloatParseHandling = FloatParseHandling.Double;
        }

        public RunServiceHttpClient(
            Uri baseUrl,
            VssOAuthCredential credentials)
            : base(baseUrl, credentials)
        {
        }

        public RunServiceHttpClient(
            Uri baseUrl,
            VssOAuthCredential credentials,
            RawClientHttpRequestSettings settings)
            : base(baseUrl, credentials, settings)
        {
        }

        public RunServiceHttpClient(
            Uri baseUrl,
            VssOAuthCredential credentials,
            params DelegatingHandler[] handlers)
            : base(baseUrl, credentials, handlers)
        {
        }

        public RunServiceHttpClient(
            Uri baseUrl,
            VssOAuthCredential credentials,
            RawClientHttpRequestSettings settings,
            params DelegatingHandler[] handlers)
            : base(baseUrl, credentials, settings, handlers)
        {
        }

        public RunServiceHttpClient(
            Uri baseUrl,
            HttpMessageHandler pipeline,
            Boolean disposeHandler)
            : base(baseUrl, pipeline, disposeHandler)
        {
        }

        public async Task<AgentJobRequestMessage> GetJobMessageAsync(
            Uri requestUri,
            string messageId,
            string runnerOS,
            CancellationToken cancellationToken = default)
        {
            HttpMethod httpMethod = new HttpMethod("POST");
            var payload = new AcquireJobRequest
            {
                JobMessageId = messageId,
                RunnerOS = runnerOS
            };

            requestUri = new Uri(requestUri, "acquirejob");

            var requestContent = new ObjectContent<AcquireJobRequest>(payload, new VssJsonMediaTypeFormatter(true));
            var result = await SendAsync<AgentJobRequestMessage>(
                httpMethod,
                requestUri: requestUri,
                content: requestContent,
                readErrorContent: true,
                cancellationToken: cancellationToken);

            if (result.IsSuccess)
            {
                return result.Value;
            }

            if (TryParseErrorContent(result.ErrorContent, out RunServiceError error))
            {
                switch ((HttpStatusCode)error.StatusCode)
                {
                    case HttpStatusCode.NotFound:
                        throw new TaskOrchestrationJobNotFoundException($"Job message not found '{messageId}'. {error.ErrorMessage}");
                    case HttpStatusCode.Conflict:
                        throw new TaskOrchestrationJobAlreadyAcquiredException($"Job message already acquired '{messageId}'. {error.ErrorMessage}");
                    case HttpStatusCode.UnprocessableEntity:
                        throw new TaskOrchestrationJobUnprocessableException($"Unprocessable job '{messageId}'. {error.ErrorMessage}");
                }
            }

            // Temporary back compat
            switch (result.StatusCode)
            {
                case HttpStatusCode.NotFound:
                    throw new TaskOrchestrationJobNotFoundException($"Job message not found: {messageId}");
                case HttpStatusCode.Conflict:
                    throw new TaskOrchestrationJobAlreadyAcquiredException($"Job message already acquired: {messageId}");
            }

            if (!string.IsNullOrEmpty(result.ErrorContent))
            {
                throw new Exception($"Failed to get job message: {result.Error}. {result.ErrorContent}");
            }
            else
            {
                throw new Exception($"Failed to get job message: {result.Error}");
            }
        }

        public async Task CompleteJobAsync(
            Uri requestUri,
            Guid planId,
            Guid jobId,
            TaskResult result,
            Dictionary<String, VariableValue> outputs,
            IList<StepResult> stepResults,
            IList<Annotation> jobAnnotations,
            string environmentUrl,
            CancellationToken cancellationToken = default)
        {
            HttpMethod httpMethod = new HttpMethod("POST");
            var payload = new CompleteJobRequest()
            {
                PlanID = planId,
                JobID = jobId,
                Conclusion = result,
                Outputs = outputs,
                StepResults = stepResults,
                Annotations = jobAnnotations,
                EnvironmentUrl = environmentUrl,
            };

            requestUri = new Uri(requestUri, "completejob");

            var requestContent = new ObjectContent<CompleteJobRequest>(payload, new VssJsonMediaTypeFormatter(true));
            var response = await SendAsync(
                    httpMethod,
                    requestUri,
                    content: requestContent,
                    cancellationToken: cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            switch (response.StatusCode)
            {
                case HttpStatusCode.NotFound:
                    throw new TaskOrchestrationJobNotFoundException($"Job not found: {jobId}");
                default:
                    throw new Exception($"Failed to complete job: {response.ReasonPhrase}");
            }
        }

        public async Task<RenewJobResponse> RenewJobAsync(
            Uri requestUri,
            Guid planId,
            Guid jobId,
            CancellationToken cancellationToken = default)
        {
            HttpMethod httpMethod = new HttpMethod("POST");
            var payload = new RenewJobRequest()
            {
                PlanID = planId,
                JobID = jobId
            };

            requestUri = new Uri(requestUri, "renewjob");

            var requestContent = new ObjectContent<RenewJobRequest>(payload, new VssJsonMediaTypeFormatter(true));
            var result = await SendAsync<RenewJobResponse>(
                httpMethod,
                requestUri,
                content: requestContent,
                cancellationToken: cancellationToken);

            if (result.IsSuccess)
            {
                return result.Value;
            }

            switch (result.StatusCode)
            {
                case HttpStatusCode.NotFound:
                    throw new TaskOrchestrationJobNotFoundException($"Job not found: {jobId}");
                default:
                    throw new Exception($"Failed to renew job: {result.Error}");
            }
        }

        protected override async Task<T> ReadJsonContentAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken = default(CancellationToken))
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<T>(json, s_serializerSettings);
        }

        private static bool TryParseErrorContent(string errorContent, out RunServiceError error)
        {
            if (!string.IsNullOrEmpty(errorContent))
            {
                try
                {
                    error = JsonUtility.FromString<RunServiceError>(errorContent);
                    if (error?.Source == "actions-run-service")
                    {
                        return true;
                    }
                }
                catch (Exception)
                {
                }
            }

            error = null;
            return false;
        }
    }
}
