using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace SearchTools {
	public class RomIncludeViewer : EditorWindow {

		/// <summary>
		/// メニュー化
		/// </summary>
		[MenuItem("Window/Search Tools/Rom Include Viewer")]
		public static void Menu() {
			var st = GetWindow<RomIncludeViewer>("Rom Include Viewer");
			st.Show();
		}

		/// <summary>
		/// 構築
		/// </summary>
		public void OnEnable() {
			EditorApplication.projectWindowItemOnGUI += ProjectWindowItemOnGUI;
			Selection.selectionChanged += Repaint;

			StartAnalyze();
		}

		/// <summary>
		/// 破棄
		/// </summary>
		public void OnDisable() {
			Selection.selectionChanged -= Repaint;
			EditorApplication.projectWindowItemOnGUI -= ProjectWindowItemOnGUI;

			QuitAnalyze();
			EditorApplication.RepaintProjectWindow();
		}

		/// <summary>
		/// 描画
		/// </summary>
		public void OnGUI() {
			Toolbar();
			if (EditorSettings.serializationMode != SerializationMode.ForceText) {
				EditorGUILayout.HelpBox("\"Editor Settings/Asset Serialization\" isn't \"Force Text\"", MessageType.Warning);
			}
			LinkView();
		}

		/// <summary>
		/// 解析モード
		/// </summary>
		private enum AnalyzeMode {
			InboundLinks,
			Links,
		}

		/// <summary>
		/// 解析モード
		/// </summary>
		private AnalyzeMode analyzeMode = AnalyzeMode.InboundLinks;

		/// <summary>
		/// モードラベル
		/// </summary>
		private GUIContent[] modeLabels = System.Enum.GetNames(typeof(AnalyzeMode)).Select(x=>new GUIContent(x)).ToArray();

		/// <summary>
		/// リンクビューステート
		/// </summary>
		private struct LinkViewState {
			public Vector2 scrollPosition;
			public Dictionary<string, bool> Foldouts;

			public static LinkViewState identity {get{
				return new LinkViewState(){scrollPosition = Vector2.zero, Foldouts = new Dictionary<string, bool>()};
			}}
		}

		/// <summary>
		/// リンクビューステート
		/// </summary>
		private LinkViewState[] linkViewStates = Enumerable.Repeat(LinkViewState.identity, System.Enum.GetValues(typeof(AnalyzeMode)).Length).ToArray();

		/// <summary>
		/// 解析器
		/// </summary>
		private LinkAnalyzer linkAnalyzer = new LinkAnalyzer();

		/// <summary>
		/// ツールバー
		/// </summary>
		/// <returns></returns>
		private Rect Toolbar() {
			GUILayout.BeginHorizontal(EditorStyles.toolbar);
			analyzeMode = (AnalyzeMode)GUILayout.SelectionGrid((int)analyzeMode, modeLabels, modeLabels.Length, EditorStyles.toolbarButton);
			GUILayout.FlexibleSpace();
			if (linkAnalyzer.progress < 1.0f) {
				var progressBarPosition = GUILayoutUtility.GetRect(60.0f, EditorStyles.toolbar.fixedHeight);
				EditorGUI.ProgressBar(progressBarPosition, linkAnalyzer.progress, "analyzing");
			}
			GUILayout.EndHorizontal();

			var toolbarHeight = EditorStyles.toolbar.fixedHeight - EditorStyles.toolbar.border.bottom;
			var result = new Rect(new Vector2(0.0f, toolbarHeight)
								, new Vector2(position.size.x, position.size.y - toolbarHeight)
								);
			return result;
		}

		/// <summary>
		/// リンクビュー
		/// </summary>
		private void LinkView() {
			if (Selection.activeObject != null) {
				using (var scrollView = new EditorGUILayout.ScrollViewScope(linkViewStates[(int)analyzeMode].scrollPosition)) {
					linkViewStates[(int)analyzeMode].scrollPosition = scrollView.scrollPosition;

					var assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
					LinkView(assetPath);
				}
			}
		}

		/// <summary>
		/// リンクビュー
		/// </summary>
		private void LinkView(string assetPath) {
			if (string.IsNullOrEmpty(assetPath)) {
				return;
			}

			List<string> nestPaths;
			switch (analyzeMode) {
			case AnalyzeMode.InboundLinks:
				nestPaths = linkAnalyzer.GetInboundLinks(assetPath);
				break;
			case AnalyzeMode.Links:
			default:
				nestPaths = linkAnalyzer.GetLinks(assetPath);
				break;
			}
			if ((nestPaths != null) && (nestPaths.Count == 0)) {
				nestPaths = null;
			}
			var spritePackingTag = linkAnalyzer.GetSpritePackingTag(assetPath);
			if (string.IsNullOrEmpty(spritePackingTag)) {
				spritePackingTag = null;
			}
			var hasChild = (nestPaths != null) || (spritePackingTag != null);

			if (!linkViewStates[(int)analyzeMode].Foldouts.ContainsKey(assetPath)) {
				linkViewStates[(int)analyzeMode].Foldouts.Add(assetPath, false);
			}
			var foldout = linkViewStates[(int)analyzeMode].Foldouts[assetPath];

			var position = GUILayoutUtility.GetRect(EditorGUIUtility.fieldWidth, EditorGUIUtility.singleLineHeight);
			if (hasChild) {
				EditorGUI.BeginChangeCheck();
				{
					var indentLevel = EditorGUI.indentLevel;
					var foldoutPosition =EditorGUI.IndentedRect(position);
					foldoutPosition.width = foldoutPosition.height;
					EditorGUI.indentLevel = 0;
					foldout = EditorGUI.Foldout(foldoutPosition, foldout, GUIContent.none);
					EditorGUI.indentLevel = indentLevel;
				}
				if (EditorGUI.EndChangeCheck()) {
					linkViewStates[(int)analyzeMode].Foldouts[assetPath] = foldout;
				}
			}
			position.xMin += position.height;
			AssetField(position, assetPath);

			if (foldout) {
				++EditorGUI.indentLevel;
				if (nestPaths != null) {
					foreach (var nestPath in nestPaths) {
						LinkView(nestPath);
					}
				}
				if (spritePackingTag != null) {
					SpritePackingTagsField(spritePackingTag);
				}

				--EditorGUI.indentLevel;
			}
		}

		/// <summary>
		/// アセットビュー
		/// </summary>
		private void AssetField(Rect position, string assetPath) {
			var includeMarkPosition = new Rect(position);
			includeMarkPosition.xMin = includeMarkPosition.xMax - position.height;
			if (linkAnalyzer.IsIncludeFromPath(assetPath) == LinkAnalyzer.IsIncludeReturn.True) {
				GUI.DrawTexture(includeMarkPosition, EditorGUIUtility.FindTexture("Collab"));
			}

			CustomGUI.ObjectLabelField(position, assetPath);
		}

		/// <summary>
		/// スプライトパッキングタグビュー
		/// </summary>
		private void SpritePackingTagsField(string tag) {
			var position = GUILayoutUtility.GetRect(EditorGUIUtility.fieldWidth, EditorGUIUtility.singleLineHeight);

			var includeMarkPosition = new Rect(position);
			includeMarkPosition.xMin = includeMarkPosition.xMax - position.height;
			if (linkAnalyzer.IsIncludeFromSpritePackingTag(tag) == LinkAnalyzer.IsIncludeReturn.True) {
				GUI.DrawTexture(includeMarkPosition, EditorGUIUtility.FindTexture("Collab"));
			}

			position =EditorGUI.IndentedRect(position);
			EditorGUI.LabelField(position, "SpriteTag " + tag);
		}

		/// <summary>
		/// ProjectWindow描画
		/// </summary>
		/// <param name="guid">GUID</param>
		/// <param name="selectionRect">選択矩形</param>
		private void ProjectWindowItemOnGUI(string guid, Rect selectionRect) {
			var path = AssetDatabase.GUIDToAssetPath(guid);
			var include = IsInclude(path);
			if (include != LinkAnalyzer.IsIncludeReturn.False) {
				var pos = selectionRect;
				pos.x = pos.xMax - pos.height;
				pos.width = pos.height;

				Texture2D image = null;
				if (include == LinkAnalyzer.IsIncludeReturn.True) {
					image = EditorGUIUtility.FindTexture("Collab");
				} else {
					image = EditorGUIUtility.FindTexture("CollabNew");
				}
				GUI.DrawTexture(pos, image);
			}
		}

		/// <summary>
		/// 梱包確認
		/// </summary>
		/// <param name="path">パス</param>
		/// <returns>true:梱包される, false:梱包されない</returns>
		private LinkAnalyzer.IsIncludeReturn IsInclude(string path) {
			return linkAnalyzer.IsIncludeFromPath(path);
		}

		/// <summary>
		/// 解析開始
		/// </summary>
		private void StartAnalyze() {
			linkAnalyzer.Start();
			EditorApplication.update += AnalyzingUpdate;
		}

		/// <summary>
		/// 解析中更新
		/// </summary>
		public void AnalyzingUpdate() {
			Repaint();
			EditorApplication.RepaintProjectWindow();

			if (!linkAnalyzer.analyzing) {
				EditorApplication.update -= AnalyzingUpdate;
			}
		}

		/// <summary>
		/// アプリケーション終了
		/// </summary>
		void QuitAnalyze() {
			EditorApplication.update -= AnalyzingUpdate;
			linkAnalyzer.Dispose();
		}
	}
}
