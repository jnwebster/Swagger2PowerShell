using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Swagger2PowerShell
{
    // this class is the "master" or "wrapper" class that orchestrates the whole ordeal
    // Swagger 1.2 is the only version currently supported
    public static class Swagger2PowerShell
    {
        private static List<VerbNounCmdlet> _cmdlets;
        private static Dictionary<string, string> _models;

        public static bool CreatePowerShellModuleFromSwaggerSpec(string swaggerJsonUri, string cmdletPrefix, string psModuleLocation)
        {
            // retrieve the swagger json file
            var jsonSpec = GetSwaggerSpec(swaggerJsonUri);

            // deserialize the json into a tree for easier processing
            var jsonTree = DeserializeSwaggerSpec(jsonSpec);

            // build our models before we traverse the api
            var modelsParent = jsonTree.GetValue("models");
            if (modelsParent != null)
            {
                BuildModelsFromJson(modelsParent);
            }

            // traverse the apis, building powershell cmdlets off of what is discovered
            var apis = jsonTree.GetValue("apis").Children();

            // iterate over the tree, building the module
            foreach (JObject api in apis)
            {
                if (api.Count == 1)
                {
                    // recurse into this api
                    if (!CreatePowerShellModuleFromSwaggerSpec(CombineApiPathWithBasePath(swaggerJsonUri, api.GetValue("path").ToString()), cmdletPrefix, psModuleLocation))
                    {
                        return false;
                    }
                }
                else
                {
                    GeneratePowerShellFromApiJson(api, cmdletPrefix, psModuleLocation);
                }
            }

            return true;
        }

        private static bool BuildModelsFromJson(JToken modelsParent)
        {
            var models = modelsParent.Children();
            foreach (JProperty model in models)
            {
                var name = model.Name;
                var propertiesList = new Dictionary<string, string>();
                var modelValue = model.Value;
                foreach (JProperty child in modelValue)
                {
                    var childName = child.Name;
                    if (childName.Equals("properties"))
                    {
                        var properties = child.Value;
                        foreach (JProperty property in properties)
                        {
                            var propertyName = property.Name;
                            string propertyType = "";
                            var propertyValues = property.Value;
                            foreach (JProperty value in propertyValues)
                            {
                                if (value.Name.Equals("type"))
                                {
                                    propertyType = value.Value.ToString();
                                }
                                if (value.Name.Equals("$ref"))
                                {
                                    propertyType = value.Value.ToString();
                                }
                                if (value.Name.Equals("enum"))
                                {
                                    GeneratePowerShellEnumFromJson(value, propertyType);
                                }
                            }
                            propertiesList.Add(propertyName, propertyType);
                        }
                    }
                }
            }
            return true;
        }

        private static void GeneratePowerShellObject(string objectName, Dictionary<string, string> properties)
        {
            // skip it if it's already there
            if (_models.ContainsKey(objectName)) return;
        }

        private static void GeneratePowerShellEnumFromJson(JProperty enumProperty, string enumName)
        {
            // skip it if it's already there
            if (_models.ContainsKey(enumName)) return;

            // build a powershell enum based on the json enum
            var enumModel = @"Add-Type -TypeDefinition @""
    public enum " + enumName + @"
    {
        ";
            var enumValues = enumProperty.Value;
            const string comma = @",
        ";
            var addComma = false;
            foreach (var enumValue in enumValues)
            {
                if (addComma) enumModel += comma;
                enumModel += enumValue.ToString();
                addComma = true;
            }
            enumModel += @"
    }
""@
";
            _models.Add(enumName, enumModel);
        }

        private static string CombineApiPathWithBasePath(string baseUri, string apiPath)
        {
            var uriBase = new Uri(baseUri);
            if (apiPath.StartsWith("/")) apiPath = apiPath.Substring(1);
            var leaf = uriBase.AbsolutePath + apiPath;
            var retVal = new Uri(uriBase, leaf).ToString();
            return retVal;
        }

        private static void GeneratePowerShellFromApiJson(JObject api, string cmdletPrefix, string psModuleLocation)
        {
            // operations is an array containing every method that can be accessed for this endpoint
            var operations = api.GetValue("operations").Children();

            foreach (JObject operation in operations)
            {
                var method = operation.GetValue("method").ToString();
            }
            
        }

        private static VerbNounCmdlet ConvertEndpointMethodToVerbNounPair(JProperty endpointMethod)
        {
            // take a swagger endpoint method and turn it into a verb noun pair

            // the verb is the method (POST/PUT/GET/DELETE/etc)
            var verb = endpointMethod.Name;

            // the noun is a little trickier
            var methodProperties = ((JObject)endpointMethod.Value).GetValue("tags");



            return new VerbNounCmdlet(verb, "");
        }

        private static bool VerbNourPairAlreadyExists(VerbNounCmdlet needle, List<VerbNounCmdlet> haystack)
        {
            return false;
        }

        private static VerbNounCmdlet BuildNewCmdletFromSwaggerPath(KeyValuePair<string, JToken> path, string cmdletPrefix)
        {
            return new VerbNounCmdlet();
        }

        private static JObject DeserializeSwaggerSpec(string jsonSpec)
        {
            // Many thanks to the Newtonsoft people, you've made my life substantially easier
            var dJson = (JObject)JsonConvert.DeserializeObject(jsonSpec);

            // extremely basic validation - just making sure it's swaggerVersion 1.2
            var version = dJson.GetValue("swaggerVersion");
            if (!version.ToString().Equals("1.2")) throw new Exception("The swagger spec is invalid. Only swagger version 1.2 is supported.");

            return dJson;
        }

        private static string GetSwaggerSpec(string swaggerDotJsonUri)
        {
            using (var client = new WebClient())
            {
                try
                {
                    var response = client.DownloadString(swaggerDotJsonUri);
                    return response;
                }
                catch (WebException)
                {
                    throw new Exception(String.Format("The specified URI: [{0}] is invalid", swaggerDotJsonUri));
                }
                catch (ArgumentNullException)
                {
                    throw new Exception("The specified URI is null, please pass a URI from which to retrieve the swagger spec.");
                }
            }
        }
    }

    internal class VerbNounCmdlet
    {
        public string Verb { get; set; }
        public string Noun { get; set; }
        public string Cmdlet { get; set; }

        public VerbNounCmdlet() {}

        public VerbNounCmdlet(string verb, string noun)
        {
            Verb = verb;
            Noun = noun;
        }

        public VerbNounCmdlet(string verb, string noun, string cmdlet)
        {
            Verb = verb;
            Noun = noun;
            Cmdlet = cmdlet;
        }
    }
}
