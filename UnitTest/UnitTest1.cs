using Composition.WindowsRuntimeHelpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using Composition.WindowsRuntimeHelpers;

namespace UnitTest {
    [TestClass]
    public class UnitTest1 {
        [TestMethod]
        public void TestGraphicsCapture() {
            // Arrange
            var hwnd = IntPtr.Zero;
            var item = CaptureHelper.CreateItemForMonitor(hwnd);
            // Act
            // Assert
            Assert.IsNotNull(item);

        }
    }
}
