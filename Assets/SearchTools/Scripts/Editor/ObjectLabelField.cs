using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace SearchTools {
	public static partial class CustomGUI {
		public static void ObjectLabelField(Rect position, Object value) {
			var style = CustomGUIDetail.ObjectLabelFieldDefaultGUIStyle();
			ObjectLabelField(position, value, style);
		}
		public static void ObjectLabelField(Rect position, Object value, GUIStyle style) {
			int controlID = GUIUtility.GetControlID(FocusType.Passive);

			switch (Event.current.GetTypeForControl(controlID)) {
			case EventType.Repaint:
				{
					GUIContent label;
					if (value != null) {
						label = EditorGUIUtility.ObjectContent(value, value.GetType());
					} else {
						label = new GUIContent("null", EditorGUIUtility.FindTexture("CollabConflict"));
					}
					EditorGUI.LabelField(position, label);
				}
				break;
			case EventType.MouseDown:
				if (position.Contains(Event.current.mousePosition) && (Event.current.button == 0)) {
					EditorGUIUtility.PingObject(value);
				}
				break;
			}
		}

		public static void ObjectLabelField(Rect position, string guid, int fileID) {
			var style = CustomGUIDetail.ObjectLabelFieldDefaultGUIStyle();
			ObjectLabelField(position, guid, fileID, style);
		}
		public static void ObjectLabelField(Rect position, string guid, int fileID, GUIStyle style) {
			var assetPath = AssetDatabase.GUIDToAssetPath(guid);
			Object value;
			if (fileID != 0) {
				value = CustomGUIDetail.LoadAllAssetsAtPath(assetPath)
									.Where(x=>Unsupported.GetLocalIdentifierInFile(x.GetInstanceID()) == fileID)
									.FirstOrDefault();
			} else {
				value = AssetDatabase.LoadMainAssetAtPath(assetPath);
			}
			ObjectLabelField(position, value, style);
		}

		public static void ObjectLabelField(Rect position, int instanceID) {
			var style = CustomGUIDetail.ObjectLabelFieldDefaultGUIStyle();
			ObjectLabelField(position, instanceID, style);
		}
		public static void ObjectLabelField(Rect position, int instanceID, GUIStyle style) {
			var value = EditorUtility.InstanceIDToObject(instanceID);
			ObjectLabelField(position, value, style);
		}

		public static void ObjectLabelField(Rect position, string assetPath) {
			var style = CustomGUIDetail.ObjectLabelFieldDefaultGUIStyle();
			ObjectLabelField(position, assetPath, style);
		}
		public static void ObjectLabelField(Rect position, string assetPath, GUIStyle style) {
			var value = AssetDatabase.LoadMainAssetAtPath(assetPath);
			ObjectLabelField(position, value, style);
		}

		public static void ObjectLabelField(Rect position, SerializedProperty property) {
			var style = CustomGUIDetail.ObjectLabelFieldDefaultGUIStyle();
			ObjectLabelField(position, property, style);
		}
		public static void ObjectLabelField(Rect position, SerializedProperty property, GUIStyle style) {
			ObjectLabelField(position, property.objectReferenceValue, style);
		}
	}

	public static partial class CustomGUILayout {
		public static void ObjectLabelField(Object value, params GUILayoutOption[] options) {
			var style = CustomGUIDetail.ObjectLabelFieldDefaultGUIStyle();
			ObjectLabelField(value, style);
		}
		public static void ObjectLabelField(Object value, GUIStyle style, params GUILayoutOption[] options) {
			var position = GUILayoutUtility.GetRect(GUIContent.none, style, options);
			CustomGUI.ObjectLabelField(position, value, style);
		}

		public static void ObjectLabelField(string guid, int fileID, params GUILayoutOption[] options) {
			var style = CustomGUIDetail.ObjectLabelFieldDefaultGUIStyle();
			ObjectLabelField(guid, fileID, style);
		}
		public static void ObjectLabelField(string guid, int fileID, GUIStyle style, params GUILayoutOption[] options) {
			var assetPath = AssetDatabase.GUIDToAssetPath(guid);
			Object value;
			if (fileID != 0) {
				value = CustomGUIDetail.LoadAllAssetsAtPath(assetPath)
									.Where(x=>Unsupported.GetLocalIdentifierInFile(x.GetInstanceID()) == fileID)
									.FirstOrDefault();
			} else {
				value = AssetDatabase.LoadMainAssetAtPath(assetPath);
			}
			ObjectLabelField(value, style);
		}

		public static void ObjectLabelField(int instanceID, params GUILayoutOption[] options) {
			var style = CustomGUIDetail.ObjectLabelFieldDefaultGUIStyle();
			ObjectLabelField(instanceID, style);
		}
		public static void ObjectLabelField(int instanceID, GUIStyle style, params GUILayoutOption[] options) {
			var value = EditorUtility.InstanceIDToObject(instanceID);
			ObjectLabelField(value, style);
		}

		public static void ObjectLabelField(string assetPath, params GUILayoutOption[] options) {
			var style = CustomGUIDetail.ObjectLabelFieldDefaultGUIStyle();
			ObjectLabelField(assetPath, style);
		}
		public static void ObjectLabelField(string assetPath, GUIStyle style, params GUILayoutOption[] options) {
			var value = AssetDatabase.LoadMainAssetAtPath(assetPath);
			ObjectLabelField(value, style);
		}

		public static void ObjectLabelField(SerializedProperty property, params GUILayoutOption[] options) {
			var style = CustomGUIDetail.ObjectLabelFieldDefaultGUIStyle();
			ObjectLabelField(property, style);
		}
		public static void ObjectLabelField(SerializedProperty property, GUIStyle style, params GUILayoutOption[] options) {
			ObjectLabelField(property.objectReferenceValue, style, options);
		}
	}

	public static partial class CustomGUIDetail {
		public static GUIStyle ObjectLabelFieldDefaultGUIStyle() {
			var result = new GUIStyle(GUI.skin.label);
			result.margin = new RectOffset();
			//result.padding = new RectOffset();
			return result;
		}

		public static IEnumerable<Object> LoadAllAssetsAtPath(string path) {
			yield return AssetDatabase.LoadMainAssetAtPath(path);
			foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path)) {
				yield return obj;
			}
		}
	}
}
