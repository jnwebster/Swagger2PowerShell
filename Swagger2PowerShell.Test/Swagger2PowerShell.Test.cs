using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Swagger2PowerShellTest
{
    [TestClass]
    public class Swagger2PowerShellTest
    {
        [TestCategory("Integration")]
        [TestMethod]
        public void ShouldDoSomething()
        {
            var response = Swagger2PowerShell.Swagger2PowerShell.CreatePowerShellModuleFromSwaggerSpec("http://webster-dev2014.madvdi.local:1234/api-docs/", "S2P", "C:\\Temp\\vWorkspace-WebAPI.ps1");
            Assert.IsTrue(response);
        }
    }
}
