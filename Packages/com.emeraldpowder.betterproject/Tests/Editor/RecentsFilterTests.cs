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
        [Test] public void IsEditWindow_FieldPickers_True()
        {
            Assert.IsTrue(RecentsFilter.IsEditWindow("UnityEditor.ObjectSelector"));
            Assert.IsTrue(RecentsFilter.IsEditWindow("UnityEditor.ColorPicker"));
            Assert.IsTrue(RecentsFilter.IsEditWindow("UnityEditor.GradientPicker"));
            Assert.IsTrue(RecentsFilter.IsEditWindow("UnityEditor.CurveEditorWindow"));
        }
        [Test] public void ShouldRecordEdit_DropOntoInspector_True()
        {
            // Drag from a project window onto an Inspector field: focus stays on the source window.
            Assert.IsTrue(RecentsFilter.ShouldRecordEdit("BetterProject.Editor.BetterProjectWindow", "UnityEditor.InspectorWindow", "Assets/M.mat"));
        }
        [Test] public void ShouldRecordEdit_MouseOverOtherWindow_False()
        {
            Assert.IsFalse(RecentsFilter.ShouldRecordEdit("Other.Editor.SomeWindow", "UnityEditor.SceneView", "Assets/M.mat"));
            Assert.IsFalse(RecentsFilter.ShouldRecordEdit(null, "UnityEditor.ObjectSelector", "Assets/M.mat")); // pickers count only focused
        }
        [Test] public void ShouldRecordEdit_NullWindow_False() => Assert.IsFalse(RecentsFilter.ShouldRecordEdit(null, "Assets/M.mat"));
        [Test] public void ShouldRecordEdit_ProjectSettings_False() => Assert.IsFalse(RecentsFilter.ShouldRecordEdit("UnityEditor.InspectorWindow", "ProjectSettings/TagManager.asset"));
    }
}
