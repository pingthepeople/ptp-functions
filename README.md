These scripts gather data about the progress of legislation in the Indiana General Assembly via its [API](http://docs.api.iga.in.gov/api.html). This can help politically-active organizations track the progress of bills that are important to them.

## Building

These scripts are written with [F#](http://fsharp.org/), a cross-platform functional language built on the .Net framework. You can find instructions for installing F# on your system at [fsharp.org](http://fsharp.org/).

## Configuration

The scripts assume the presence of the following environment variables:

* `IgaApiKey`: an API key for the Indiana General Assembly API
* 'SendGridApiKey`: an API key for SendGrid with permission to send email
* `WindowsAzure.StorageAccount`: a connection string to a Microsoft Azure storage account
* `EmailRecipients`: a semicolon-separated list of email addresses that should receive updates 

## Deployment

The scripts are intended to be run as [Azure Functions](https://azure.microsoft.com/en-us/services/functions/). After creating your Function App and cloning this repository, you can connect the Function App to your GitHub repo. It will automatically detect changes and deploy the functions.
