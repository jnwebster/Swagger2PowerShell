# Swagger2PowerShell
REST endpoint that turns a Swagger 1.2 spec into a PowerShell module so you can script your REST api

This was my project for Build Dell 2015

It's a C# dll that can be dropped into a Swagger-documented (version 1.2 only for now) RESTful web api (running on an IIS server with .NET 4.5 installed) that provides an endpoint for it to live on.
When the endpoint is hit, Swagger2PowerShell examines the provided Swagger documentation and constructs a PowerShell module based on the exposed endpoints.

The primary benefit to this is that if you're providing both a web api and a PowerShell module, the two currently need to be maintained by their own teams.
With this piece in place, you can take the effort that's being put into maintaining the PowerShell module and focus it on some other part of the product.
When something in the product gets updated, you only need to update the web api and notify anyone who is using it to re-download the PowerShell module to get the updates.

Additionally, if you don't have the resources in place to develop and maintain a separate PowerShell module for your web api, now you have no excuse - just drop this in and your users will be scripting in no time.

Right now the dll is quite rough around the edges (what else do you expect for a product thrown together in 24 hours by one guy?) and has many kludges and hacky fixes. My plans for the future of this project are:
* Make it robust - right now there are some elements of the Swagger spec that it doesn't know what to do with, I want it to be able to handle anything that the Swagger spec can throw at it
* Make it extendable - add the ability to handle the up-and-coming Swagger 2.0 spec and switch painlessly between them, and also look into supporting other specs as well (RAML, etc)
* Make it efficient - there are plenty of places where it need optimization, and even more places where a hack needs a more elegant solution
* Make it customizable - right now there is no way to specify verb/noun pairs for endpoints (in my example, the authorize user endpoint becomes Add-AuthRequest which is not intuitive) so I want to be able to provide a document Swagger2PowerShell checks to see if you want to override the default verb/noun pair for any given endpoint
