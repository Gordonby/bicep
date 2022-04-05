// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.Resources.Models;
using Bicep.Core;
using Bicep.Core.Json;

namespace Bicep.LanguageServer.Deploy
{
    public class DeploymentHelper
    {
        /// <summary>
        /// Creates a deployment at provided target scope and returns deployment succeeded/failed message.
        /// </summary>
        /// <param name="deploymentCollectionProvider">deployment collection provider</param>
        /// <param name="armClient">arm client</param>
        /// <param name="documentPath">path to bicep file used in deployment</param>
        /// <param name="template">template used in deployment</param>
        /// <param name="parameterFilePath">path to parameter file used in deployment</param>
        /// <param name="id">id string to create the ResourceIdentifier from</param>
        /// <param name="scope">target scope</param>
        /// <param name="location">location to store the deployment data</param>
        /// <returns>deployment succeeded/failed message and result</returns>
        public static async Task<(string, string)> CreateDeployment(
            IDeploymentCollectionProvider deploymentCollectionProvider,
            ArmClient armClient,
            string documentPath,
            string template,
            string parameterFilePath,
            string id,
            string scope,
            string location)
        {
            if ((scope == LanguageConstants.TargetScopeTypeSubscription ||
                scope == LanguageConstants.TargetScopeTypeManagementGroup) &&
                string.IsNullOrWhiteSpace(location))
            {
                return (LangServerResources.DeploymentFailedResult, string.Format(LangServerResources.MissingLocationDeploymentFailedMessage, documentPath));
            }

            DeploymentCollection? deploymentCollection;
            var resourceIdentifier = new ResourceIdentifier(id);

            try
            {
                deploymentCollection = deploymentCollectionProvider.GetDeploymentCollection(armClient, resourceIdentifier, scope);
            }
            catch (Exception e)
            {
                return (LangServerResources.DeploymentFailedResult, string.Format(LangServerResources.DeploymentFailedWithExceptionMessage, documentPath, e.Message));
            }

            if (deploymentCollection is not null)
            {
                JsonElement parameters;

                try
                {
                    parameters = GetParameters(documentPath, parameterFilePath);
                }
                catch (Exception e)
                {
                    return (LangServerResources.DeploymentFailedResult, e.Message);
                }

                var deploymentProperties = new DeploymentProperties(DeploymentMode.Incremental)
                {
                    Template = JsonDocument.Parse(template).RootElement,
                    Parameters = parameters
                };
                var input = new DeploymentInput(deploymentProperties)
                {
                    Location = location,
                };

                string deployment = "bicep_deployment_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");

                try
                {
                    var deploymentCreateOrUpdateOperation = await deploymentCollection.CreateOrUpdateAsync(waitForCompletion:true, deployment, input);

                    return GetDeploymentResultMessage(deploymentCreateOrUpdateOperation, documentPath);
                }
                catch (Exception e)
                {
                    return (LangServerResources.DeploymentFailedResult, string.Format(LangServerResources.DeploymentFailedWithExceptionMessage, documentPath, e.Message));
                }
            }

            return (LangServerResources.DeploymentFailedResult, string.Format(LangServerResources.DeploymentFailedMessage, documentPath));
        }

        private static (string, string) GetDeploymentResultMessage(DeploymentCreateOrUpdateOperation deploymentCreateOrUpdateOperation, string documentPath)
        {
            if (!deploymentCreateOrUpdateOperation.HasValue)
            {
                return (LangServerResources.DeploymentFailedResult, string.Format(LangServerResources.DeploymentFailedMessage, documentPath));
            }

            var response = deploymentCreateOrUpdateOperation.GetRawResponse();
            var status = response.Status;

            if (status == 200 || status == 201)
            {
                return (LangServerResources.DeploymentSucceededResult, string.Format(LangServerResources.DeploymentSucceededMessage, documentPath));
            }
            else
            {
                return (LangServerResources.DeploymentFailedResult, string.Format(LangServerResources.DeploymentFailedWithExceptionMessage, documentPath, response.ToString()));
            }
        }

        private static JsonElement GetParameters(string documentPath, string parameterFilePath)
        {
            if (string.IsNullOrWhiteSpace(parameterFilePath))
            {
                return JsonElementFactory.CreateElement("{}");
            }
            else
            {
                try
                {
                    string text = File.ReadAllText(parameterFilePath);
                    return JsonElementFactory.CreateElement(text);
                }
                catch (Exception e)
                {
                    throw new Exception(string.Format(LangServerResources.InvalidParameterFileDeploymentFailedMessage, documentPath, e.Message));
                }
            }
        }
    }
}
