using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Swagger2PowerShell
{
    [TestClass]
    public class Swagger2PowerShellTest
    {
        [TestCategory("Integration")]
        [TestMethod]
        public void ShouldDoSomething()
        {
            var response = Swagger2PowerShell.CreatePowerShellModuleFromSwaggerSpec("http://petstore.swagger.io/v2/swagger.json", "SPS", "C:\\Temp\\");
            Assert.IsTrue(response);
        }
    }
}
