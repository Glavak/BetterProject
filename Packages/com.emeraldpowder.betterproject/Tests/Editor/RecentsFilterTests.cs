using NUnit.Framework;

namespace BetterProject.Core.Tests
{
    public class RecentsFilterTests
    {
        [Test] public void ShouldRecordEdit_InspectorAsset_True()
        {
            Assert.IsTrue(RecentsFilter.ShouldRecordEdit("UnityEditor.InspectorWindow", "Assets/M.mat"));
            Assert.IsTrue(RecentsFilter.ShouldRecordEdit("UnityEditor.PropertyEditor", "Assets/M.mat"));
        }
        [Test] public void ShouldRecordEdit_OtherWindow_False()
        {
            Assert.IsFalse(RecentsFilter.ShouldRecordEdit("BetterProject.Editor.BetterProjectWindow", "Assets/M.mat"));
            Assert.IsFalse(RecentsFilter.ShouldRecordEdit("Other.Editor.SomeWindow", "Assets/M.mat"));
        }
        [Test] public void IsEditWindow_InspectorAndProperties_True()
        {
            Assert.IsTrue(RecentsFilter.IsEditWindow("UnityEditor.InspectorWindow"));
            Assert.IsTrue(RecentsFilter.IsEditWindow("UnityEditor.PropertyEditor"));
        }
        [Test] public void IsEditWindow_OtherOrNull_False()
        {
            Assert.IsFalse(RecentsFilter.IsEditWindow("Other.Editor.SomeWindow"));
            Assert.IsFalse(RecentsFilter.IsEditWindow("UnityEditor.SceneView"));
            Assert.IsFalse(RecentsFilter.IsEditWindow(null));
            Assert.IsFalse(RecentsFilter.IsEditWindow(""));
        }
        [Test] public void ShouldRecordEdit_NullWindow_False() => Assert.IsFalse(RecentsFilter.ShouldRecordEdit(null, "Assets/M.mat"));
        [Test] public void ShouldRecordEdit_ProjectSettings_False() => Assert.IsFalse(RecentsFilter.ShouldRecordEdit("UnityEditor.InspectorWindow", "ProjectSettings/TagManager.asset"));
    }
}
