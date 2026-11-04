# Sheridan.Aspire.Pulumi.Azure.ApplicationInsights

_Application Insights_ — part of [Sheridan.Aspire.Pulumi](../../README.md).

`builder.AddPulumiApplicationInsights("appinsights")` (project `Sheridan.Aspire.Pulumi.Azure.ApplicationInsights`) ports `AddAzureApplicationInsights`: a `web` Application Insights component (`azure-native:applicationinsights:Component`) backed by a Log Analytics workspace, with outputs `appInsightsConnectionString`, `name`, `id`. Without a workspace, a PerGB2018 workspace is created in the same resource group (as the official Bicep does). Link an existing one with `.WithLogAnalyticsWorkspace(law)` (a `AddPulumiLogAnalyticsWorkspace` resource bound to the same stack). The connection string is the resource's connection string, so `.WithReference(appInsights)` yields `ConnectionStrings__appinsights`.
