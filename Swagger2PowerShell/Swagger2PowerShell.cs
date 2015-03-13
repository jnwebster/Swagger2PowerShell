using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
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
        private static int _recurseDepth;

        static Swagger2PowerShell()
        {
            _recurseDepth = 0;
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

            // iterate over the tree, building the list of cmdlets
            foreach (JObject api in apis)
            {
                if (api.Count == 1)
                {
                    // this api only contains one node: the path to another api - RECURSE!
                    _recurseDepth++;
                    if (!CreatePowerShellModuleFromSwaggerSpec(CombineApiPathWithBasePath(swaggerJsonUri, api.GetValue("path").ToString()), cmdletPrefix, psModuleLocation))
                    {
                        return false;
                    }
                }
                else
                {
                    GenerateCmdletsFromApiJson(api);
                }
            }

            // at this point _cmdlets has been filled with all discoverable operations from this api
            // check _recurseDepth to see if we're finished with everything
            if (_recurseDepth == 0)
            {
                // all finished - we're ready to turn our list of cmdlet outlines into actual powershell now
                var powershellCode = GeneratePowerShellCode(cmdletPrefix);
            }
            else
            {
                _recurseDepth--;
            }

            return true;
        }

        private static string GeneratePowerShellCode(string cmdletPrefix)
        {
            var code = "";
            // pull in the models and any cmdlets that have been generated
            foreach (var model in _models)
            {
                code += model.Value;
                code += @"

";
            }
            foreach (var cmdlet in _cmdlets)
            {
                if (!string.IsNullOrEmpty(cmdlet.Cmdlet))
                {
                    code += cmdlet.Cmdlet;
                    code += @"

";
                }
            }

            // go through all cmdlets, combining those with the same verb/noun
            code += BuildPowerShellFromCmdlets(cmdletPrefix);

            return code;
        }

        private static string BuildPowerShellFromCmdlets(string cmdletPrefix)
        {
            // count how many verb/noun sets there are
            // 1 = just make a simple cmdlet
            // n = make a parameter set cmdlet

            var verbNounPairs = CountVerbNounPairs();

            string code = "";

            foreach (var pair in verbNounPairs)
            {
                if (pair.Value == 1)
                {
                    code += GenerateSimpleCmdletFor(pair.Key, cmdletPrefix);
                }
                else
                {
                    code += GenerateParamSetCmdletFor(pair.Key, cmdletPrefix);
                }
                code += @"

";
            }

            return code;
        }

        private static string GenerateParamSetCmdletFor(KeyValuePair<string, string> verbNoun, string cmdletPrefix)
        {
            string code = "";

            return code;
        }

        private static string GenerateSimpleCmdletFor(KeyValuePair<string, string> verbNoun, string cmdletPrefix)
        {
            var cmdletToBuild = _cmdlets.First(c => c.Verb.Equals(verbNoun.Key) && c.Noun.Equals(verbNoun.Value));
            var code = @"function " + cmdletToBuild.Verb + @"-" + cmdletPrefix + cmdletToBuild.Noun + @"
{
    param(";
            var addComma = false;
            foreach (var param in cmdletToBuild.ParameterSet)
            {
                if (addComma) code += @",";
                code += @"
        [" + param.Value + @"]$" + param.Key;
                addComma = true;
            }
            code += @"
    )
    Begin
    {
        ";
            // Invoke-RestMethod -Uri
            code += @"
    }
}
";
            return code;
        }

        private static List<KeyValuePair<KeyValuePair<string, string>, int>> CountVerbNounPairs()
        {
            var pairs = new List<KeyValuePair<KeyValuePair<string, string>, int>>();

            foreach (var cmdlet in _cmdlets)
            {
                // only count the ones we haven't already added
                if (string.IsNullOrEmpty(cmdlet.Cmdlet))
                {
                    // are there any entries in the list that match this yet?
                    var thisPairExists = pairs.Any(p => p.Key.Equals(new KeyValuePair<string, string>(cmdlet.Verb, cmdlet.Noun)));
                    if (thisPairExists)
                    {
                        // KeyValuePairs are immutable - can't just go and ++ the value
                        var thisPair = pairs.First(p => p.Key.Equals(new KeyValuePair<string, string>(cmdlet.Verb, cmdlet.Noun)));
                        var incrementedPair = new KeyValuePair<KeyValuePair<string, string>, int>(thisPair.Key, thisPair.Value + 1);
                        pairs.Remove(thisPair);
                        pairs.Add(incrementedPair);
                    }
                    else
                    {
                        pairs.Add(new KeyValuePair<KeyValuePair<string, string>, int>(new KeyValuePair<string, string>(cmdlet.Verb, cmdlet.Noun), 1));
                    }
                }
            }

            return pairs;
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

        private static void GenerateCmdletsFromApiJson(JObject api)
        {
            var endpoint = api.GetValue("path").ToString();
            var nounAndParameters = GetNounAndParametersFromEndpoint(endpoint);

            // operations is an array containing every method that can be accessed for this endpoint
            var operations = api.GetValue("operations").Children();

            foreach (JObject operation in operations)
            {
                ConvertOperationToCmdlet(operation, nounAndParameters, endpoint);
            }

            // at this point, we have turned all available operations in this api into cmdlets
        }

        private static List<KeyValuePair<string, string>> GetNounAndParametersFromEndpoint(string endpoint)
        {
            var path = endpoint.Split(Convert.ToChar("/"));
            var nodeCount = path.Count();

            // first string = param name, second string = param type
            // noun will always come first
            // we use a List of KeyValuePairs because order matters, and a Dictionary does not preserve order
            var nounAndParameters = new List<KeyValuePair<string, string>>();
            
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
                    // the else would be if we found a {...} but weren't expecting one - two names in a row?
                    // skip it, that's too confusing
                }
                else
                {
                    if (state == PathSearchingState.Beginning)
                    {
                        // found the noun
                        nounAndParameters.Add(new KeyValuePair<string, string>("noun", path[x]));
                        state = PathSearchingState.LookingForParameterName;
                        if (thisParamName != "")
                        {
                            // parameter came first, add the parameter now that we know its type (the noun)
                            nounAndParameters.Add(new KeyValuePair<string, string>(thisParamName, path[x]));
                            thisParamName = "";
                        }
                    }
                    else if (state == PathSearchingState.LookingForParameterType)
                    {
                        nounAndParameters.Add(new KeyValuePair<string, string>(thisParamName, path[x]));
                        thisParamName = "";
                        state = PathSearchingState.LookingForParameterName;
                    }
                    else if (state == PathSearchingState.LookingForParameterName)
                    {
                        // this happens if we got two types in a row
                        // the only time this is valid is right -after- the noun is discovered, and it indicates that what we
                        // thought was the noun is actually a switch/filtering parameter and the second type is really the noun
                        if (nounAndParameters.Count == 1)
                        {
                            var switchName = nounAndParameters[0].Value;
                            nounAndParameters.Clear();
                            nounAndParameters.Add(new KeyValuePair<string, string>("noun", path[x]));
                            nounAndParameters.Add(new KeyValuePair<string, string>(switchName, "SwitchParam"));
                        }
                    }
                }
            }
            return nounAndParameters;
        }

        private static void ConvertOperationToCmdlet(JObject operation, List<KeyValuePair<string, string>> nounAndParameters, string endpoint)
        {
            // take a swagger endpoint method and turn it into the outline for a cmdlet

            // retrieve the noun
            var noun = nounAndParameters.First(p => p.Key.Equals("noun")).Value;

            // extract the verb
            var verb = ConvertRestMethodToPowerShellVerb(operation.GetValue("method").ToString());

            // get any further parameters as specified by the method
            var additionalParameters = GetMethodSpecificParameters(operation.GetValue("parameters"));

            if (additionalParameters != null && additionalParameters.Count > 0)
            {
                // concatenate the two lists
                nounAndParameters.AddRange(additionalParameters);
            }

            // configure the cmdlet with what we know now
            var cmdletToAdd = new VerbNounCmdlet(verb, noun)
            {
                Endpoint = endpoint,
                ParameterSetName = DetermineParameterSetName(nounAndParameters, noun),
                ParameterSet = nounAndParameters
            };

            _cmdlets.Add(cmdletToAdd);
        }

        private static string DetermineParameterSetName(List<KeyValuePair<string, string>> allParametersForThisOperation, string noun)
        {
            // param 2 is the same type as the noun - "byID"
            // param 2 is a switchparam - name it after the switch
            // anything else - "all"
            if (allParametersForThisOperation.Count > 1)
            {
                var secondParam = allParametersForThisOperation[1];
                if (secondParam.Value.Equals(noun))
                {
                    return "byID";
                }
                else if (secondParam.Value.Equals("SwitchParam"))
                {
                    return secondParam.Key;
                }
            }
            return "all";
        }

        private static List<KeyValuePair<string, string>> GetMethodSpecificParameters(JToken parameters)
        {
            if (parameters == null) return null;
            var children = parameters.Children();
            var additionalParameters = new List<KeyValuePair<string, string>>();

            foreach (JObject parameter in children)
            {
                additionalParameters.Add(new KeyValuePair<string, string>(parameter.GetValue("name").ToString(), parameter.GetValue("type").ToString()));
            }

            return additionalParameters;
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
        public string ParameterSetName { get; set; }
        public List<KeyValuePair<string, string>> ParameterSet { get; set; }
        public string Endpoint { get; set; }

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
