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

        static Swagger2PowerShell()
        {
            _cmdlets = new List<VerbNounCmdlet>();
            _models = new Dictionary<string, string>();
        }

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
                if (!BuildModelsFromJson(modelsParent, cmdletPrefix))
                {
                    return false;
                }
            }

            // traverse the apis, building powershell cmdlets off of what is discovered
            var apis = jsonTree.GetValue("apis").Children();

            // iterate over the tree, building the module
            foreach (JObject api in apis)
            {
                if (api.Count == 1)
                {
                    // this api only contains one node: the path to another api - RECURSE!
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

        private static bool BuildModelsFromJson(JToken modelsParent, string cmdletPrefix)
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
                        break;
                    }
                }
                GeneratePowerShellObjectFromJson(name, propertiesList, cmdletPrefix);
            }
            return true;
        }

        private static void GeneratePowerShellObjectFromJson(string objectName, Dictionary<string, string> properties, string cmdletPrefix)
        {
            // skip it if it's already there
            if (_models.ContainsKey(objectName)) return;

            // build a C# class that powershell can use based on the json object
            var objectModel = @"Add-Type -Language CSharp @""
    public class " + objectName + @"{";
            foreach (var property in properties)
            {
                objectModel += @"
        public " + property.Value + @" " + property.Key + @";";
            }
            objectModel += @"
    }
""@
";
            _models.Add(objectName, objectModel);

            // okay, we've added the class, now make a helper cmdlet for creating an instance of one
            var cmdletModel = @"function New-" + cmdletPrefix + objectName + @"
{
    param(";
            var addComma = false;
            foreach (var property in properties)
            {
                if (addComma) cmdletModel += @",";
                cmdletModel += @"
        [" + property.Value + @"]$" + property.Key;
                addComma = true;
            }
            cmdletModel += @"
    )
    Begin
    {
        $obj = New-Object " + objectName + @";";
            foreach (var property in properties)
            {
                cmdletModel += @"
        $obj." + property.Key + @" = " + property.Key + @";";
            }
            cmdletModel += @"
        $obj
    }
}
";
            _cmdlets.Add(new VerbNounCmdlet("New", objectName, cmdletModel));
        }

        private static void GeneratePowerShellEnumFromJson(JProperty enumProperty, string enumName)
        {
            // skip it if it's already there
            if (_models.ContainsKey(enumName)) return;

            // build a C# enum that powershell can use based on the json enum
            var enumModel = @"Add-Type -Language CSharp @""
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
            var nounAndParameters = GetNounAndParametersFromApiPath(api);

            // operations is an array containing every method that can be accessed for this endpoint
            var operations = api.GetValue("operations").Children();

            foreach (JObject operation in operations)
            {
                ConvertOperationToCmdlet(operation, cmdletPrefix);
            }
            
        }

        private static Dictionary<string, string> GetNounAndParametersFromApiPath(JObject api)
        {
            var path = api.GetValue("path").ToString().Split(Convert.ToChar("/"));
            var nodeCount = path.Count();

            // first string = param name, second string = param type
            // noun will always come first
            var nounAndParameters = new Dictionary<string, string>();
            
            // start at the end of the path and work backwards
            // the first node is the noun (unless it's {id}, in which case the next one is)
            // every node marked by {...} is a parameter name, with the subsequent node its type
            // if a node is not preceded by {...} then skip it
            var thisParamName = "";
            var state = PathSearchingState.Beginning;
            for (var x = (nodeCount - 1); x >= 0; x--)
            {
                if (path[x].Contains("{") && path[x].Contains("}"))
                {
                    if (state == PathSearchingState.Beginning)
                    {
                        // found a parameter first - DON'T CHANGE STATE YET
                        thisParamName = path[x];
                    }
                    else if (state == PathSearchingState.LookingForParameterName)
                    {
                        thisParamName = path[x];
                        state = PathSearchingState.LookingForParameterType;
                    }
                    // the else would be if we found a {...} but weren't expecting one - two ids in a row?
                    // skip it, that's too confusing
                }
                else
                {
                    if (state == PathSearchingState.Beginning)
                    {
                        // found the noun
                        nounAndParameters.Add("noun", path[x]);
                        state = PathSearchingState.LookingForParameterName;
                        if (thisParamName != "")
                        {
                            // parameter came first, add the parameter now that we know its type (the noun)
                            nounAndParameters.Add(thisParamName, path[x]);
                            thisParamName = "";
                        }
                    }
                    else if (state == PathSearchingState.LookingForParameterType)
                    {
                        nounAndParameters.Add(thisParamName, path[x]);
                        thisParamName = "";
                        state = PathSearchingState.LookingForParameterName;
                    }
                }
            }
            return nounAndParameters;
        }

        private static VerbNounCmdlet ConvertOperationToCmdlet(JObject operation, string cmdletPrefix)
        {
            // take a swagger endpoint method and turn it into a verb noun pair

            // extract the verb
            var verb = ConvertRestMethodToPowerShellVerb(operation.GetValue("method").ToString());



            return new VerbNounCmdlet(verb, "");
        }

        private static string ConvertRestMethodToPowerShellVerb(string restMethod)
        {
            switch (restMethod)
            {
                case "GET":
                    return "Get";
                case "PUT":
                    return "Set";
                case "POST":
                    return "Add";
                case "DELETE":
                    return "Remove";
                default:
                    throw new Exception(String.Format("Couldn't translate RESTful method verb [{0}] to a suitable PowerShell verb.", restMethod));
            }
        }

        private static bool VerbNourPairAlreadyExists(VerbNounCmdlet needle, List<VerbNounCmdlet> haystack)
        {
            return false;
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

    internal enum PathSearchingState
    {
        Beginning,
        LookingForParameterName,
        LookingForParameterType
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
