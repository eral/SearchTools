using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Reflection;

public class SearchTools : EditorWindow {

	/// <summary>
	/// メニュー化
	/// </summary>
	[MenuItem("Window/Search Tools")]
	public static void Menu() {
		var st = GetWindow<SearchTools>("Search Tools");
		st.Show();
	}

	/// <summary>
	/// 構築
	/// </summary>
	public void OnEnable() {
		mComponentTypes = getAllComponentTypes().ToArray();
	}

	/// <summary>
	/// 描画
	/// </summary>
	public void OnGUI() {
		OnGUIForToolbar();
		switch (mCurrentToolIndex) {
		case ToolIndex.Component:
			OnGUIForComponent();
			break;
		case ToolIndex.Resource:
			OnGUIForResource();
			break;
		case ToolIndex.Option:
			OnGUIForOption();
			break;
		}
	}

	/// <summary>
	/// 描画(ツールバー)
	/// </summary>
	public void OnGUIForToolbar() {
		EditorGUILayout.BeginHorizontal();
		{
			{ //Tool Part
				var labels = Enumerable.Range(0, (int)ToolIndex.Option)
											.Select(x=>((ToolIndex)x).ToString())
											.ToArray();
				var index = ((mCurrentToolIndex != ToolIndex.Option)? (int)mCurrentToolIndex: -1);
				EditorGUI.BeginChangeCheck();
				index = GUILayout.Toolbar(index, labels);
				if (EditorGUI.EndChangeCheck()) {
					mCurrentToolIndex = (ToolIndex)index;
					reset();
				}
			}
			{ //Option Part
				var labels = new[]{ToolIndex.Option.ToString()};
				var index = ((mCurrentToolIndex == ToolIndex.Option)? (int)mCurrentToolIndex - (int)ToolIndex.Option: -1);
				EditorGUI.BeginChangeCheck();
				index = GUILayout.Toolbar(index, labels);
				if (EditorGUI.EndChangeCheck()) {
					mCurrentToolIndex = (ToolIndex)(index + ToolIndex.Option);
				}
			}
		}
		EditorGUILayout.EndHorizontal();
	}

	/// <summary>
	/// 描画(コンポーネント)
	/// </summary>
	public void OnGUIForComponent() {
		EditorGUI.BeginChangeCheck();
		GUI.SetNextControlName("ComponentType");
		mComponentType = (MonoScript)EditorGUILayout.ObjectField("Component Type", mComponentType, typeof(MonoScript), false);
		if (EditorGUI.EndChangeCheck()) {
			mComponentName = ((mComponentType != null)? mComponentType.GetClass().FullName: string.Empty);
		}
		mComponentNameSearch = EditorGUILayout.Foldout(mComponentNameSearch, "Name Search");
		if (mComponentNameSearch) {
			var nameSearchStyle = new GUIStyle();
			var margin = nameSearchStyle.margin;
			margin.top = 0;
			margin.left += 12;
			margin.bottom += 8;
			nameSearchStyle.margin = margin;
			EditorGUILayout.BeginVertical(nameSearchStyle);
			//パスフィルタ欄
			mComponentName = EditorGUILayout.TextField("Component Name", mComponentName);
			//候補コンポーネント取得
			var componentTypes = getComponentTypes(mComponentName).ToArray();
			//サジェスト列挙
			if (!string.IsNullOrEmpty(mComponentName)) {
				foreach (var type in componentTypes.Take(10)) {
					if (GUILayout.Button(type.FullName, EditorStyles.miniButton)) {
						mComponentType = getMonoScript(type);
						mComponentName = type.FullName;
						GUI.FocusControl("ComponentType");
					}
				}
				if (10 < componentTypes.Length) {
					var label = string.Format("▾({0})", componentTypes.Length - 10);
					var labelStyle = new GUIStyle(EditorStyles.label);
					labelStyle.alignment = TextAnchor.UpperRight;
					EditorGUILayout.LabelField(label, labelStyle);
				}
			}
			EditorGUILayout.EndVertical();
		}
		//検索場所
		mLookIn = (LookIn)EditorGUILayout.EnumMaskField("Look In", mLookIn);
		//検索ボタン
		var oldGuiEnabled = GUI.enabled;
		GUI.enabled = (mComponentType != null); //候補が無いなら押せない
		if (GUILayout.Button("Search")) {
			searchComponent(mComponentType.GetClass());
		}
		GUI.enabled = true;
		if (GUILayout.Button("Reset")) {
			reset();
		}
		GUI.enabled = oldGuiEnabled;
		//プレファブ結果
		if (mPrefabsPreservingTarget != null) {
			foreach (var prefab in mPrefabsPreservingTarget) {
				EditorGUILayout.ObjectField(prefab, typeof(GameObject), false);
			}
		}
		//シーン結果
		if (mScenesPreservingTarget != null) {
			foreach (var prefab in mScenesPreservingTarget) {
				EditorGUILayout.ObjectField(prefab, typeof(Object), false);
			}
		}
	}

	/// <summary>
	/// 描画(リソース)
	/// </summary>
	public void OnGUIForResource() {
		EditorGUI.BeginChangeCheck();
		mResource = EditorGUILayout.ObjectField("Resource", mResource, typeof(Object), false);
		if (EditorGUI.EndChangeCheck()) {
		}
		//検索場所
		mLookIn = (LookIn)EditorGUILayout.EnumMaskField("Look In", mLookIn);
		//検索ボタン
		var oldGuiEnabled = GUI.enabled;
		GUI.enabled = (mResource != null); //候補が無いなら押せない
		if (GUILayout.Button("Search")) {
			searchResource(mResource);
		}
		GUI.enabled = true;
		if (GUILayout.Button("Reset")) {
			reset();
		}
		GUI.enabled = oldGuiEnabled;
		//プレファブ結果
		if (mPrefabsPreservingTarget != null) {
			foreach (var prefab in mPrefabsPreservingTarget) {
				EditorGUILayout.ObjectField(prefab, typeof(GameObject), false);
			}
		}
		//シーン結果
		if (mScenesPreservingTarget != null) {
			foreach (var prefab in mScenesPreservingTarget) {
				EditorGUILayout.ObjectField(prefab, typeof(Object), false);
			}
		}
	}

	/// <summary>
	/// 描画(オプション)
	/// </summary>
	public void OnGUIForOption() {
		EditorGUILayout.HelpBox("Not Implemented", MessageType.Info);
	}

	/// <summary>
	/// ツールインデックス
	/// </summary>
	private enum ToolIndex {
		Component,
		Resource,
		Option,
	}

	/// <summary>
	/// 検索場所
	/// </summary>
	[System.Flags]
	private enum LookIn {
		Prefab	= 1 << 0,
		Scene	= 1 << 1,
	}

	/// <summary>
	/// 選択中のツール
	/// </summary>
	private ToolIndex mCurrentToolIndex = 0;

	/// <summary>
	/// 検索コンポーネントのMonoScript
	/// </summary>
	private MonoScript mComponentType = null;

	/// <summary>
	/// コンポーネント名前検索を開いているか
	/// </summary>
	private bool mComponentNameSearch = false;

	/// <summary>
	/// コンポーネント名パスフィルタ
	/// </summary>
	private string mComponentName = string.Empty;

	/// <summary>
	/// ターゲットを格納しているプレファブ群
	/// </summary>
	private GameObject[] mPrefabsPreservingTarget = null;

	/// <summary>
	/// ターゲットを格納しているシーン群
	/// </summary>
	private Object[] mScenesPreservingTarget = null;

	/// <summary>
	/// 検索場所
	/// </summary>
	private LookIn mLookIn = LookIn.Prefab | LookIn.Scene;

	/// <summary>
	/// コンポーネント型
	/// </summary>
	private System.Type[] mComponentTypes = null;

	/// <summary>
	/// 検索リソース
	/// </summary>
	private Object mResource = null;

	/// <summary>
	/// リセット
	/// </summary>
	private void reset() {
		mPrefabsPreservingTarget = null;
		mScenesPreservingTarget = null;
	}

	/// <summary>
	/// MonoScriptの取得
	/// </summary>
	/// <param name="type">型</param>
	/// <returns>MonoScript</returns>
	private static MonoScript getMonoScript(System.Type type) {
		var go = new GameObject(string.Empty, type);
		var result = MonoScript.FromMonoBehaviour((MonoBehaviour)go.GetComponent(type)) ;
		DestroyImmediate(go);
		return result;
	}

	/// <summary>
	/// コンポーネント検索
	/// </summary>
	/// <param name="componentType">検索コンポーネント型</param>
	private void searchComponent(System.Type componentType) {
		reset();
		if ((mLookIn & LookIn.Prefab) != 0) {
			searchComponentInPrefabs(componentType);
		}
		if ((mLookIn & LookIn.Scene) != 0) {
			searchComponentInScenes(componentType);
		}
	}

	/// <summary>
	/// 空のシーン作成
	/// </summary>
	/// <param name="isForce">強行するか</param>
	/// <returns>true:成功、false:キャンセル</returns>
	private bool createEmptyScene(bool isForce) {
		var result = false;
		if (isForce || EditorApplication.SaveCurrentSceneIfUserWantsTo()) {
			EditorApplication.NewEmptyScene();
			result = true;
		}
		return result;
	}

	/// <summary>
	/// プレファブ内コンポーネント検索
	/// </summary>
	/// <param name="componentType">検索コンポーネント型</param>
	private void searchComponentInPrefabs(System.Type componentType) {
		if (createEmptyScene(false)) {
			var prefabPaths = getAllPrefabPaths();
			var progressWeight = 1.0f / prefabPaths.Count();
			mPrefabsPreservingTarget = prefabPaths.TakeWhile((x,i)=>!EditorUtility.DisplayCancelableProgressBar("Search component in prefabs", x, i * progressWeight))
												.Where(x=>hasComponentInPrefabPath(x, componentType))
												.Select(x=>(GameObject)AssetDatabase.LoadAssetAtPath(x, typeof(GameObject)))
												.ToArray();
			createEmptyScene(true);
			EditorUtility.ClearProgressBar();
		}
	}

	/// <summary>
	/// プレファブ内のコンポーネント格納確認
	/// </summary>
	/// <param name="path">プレファブパス</param>
	/// <param name="componentType">検索コンポーネント型</param>
	/// <returns>true:格納している、false:格納していない</returns>
	/// <remarks>シーンを汚すので注意</remarks>
	private bool hasComponentInPrefabPath(string path, System.Type componentType) {
		var gc = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath(path, typeof(GameObject)));
		var result = 0 < gc.GetComponentsInChildren(componentType, true).Length;
		DestroyImmediate(gc);
		return result;
	}

	/// <summary>
	/// シーン内コンポーネント検索
	/// </summary>
	/// <param name="componentType">検索コンポーネント型</param>
	private void searchComponentInScenes(System.Type componentType) {
		if (createEmptyScene(false)) {
			var scenePaths = getAllScenePaths();
			var progressWeight = 1.0f / scenePaths.Count();
			mScenesPreservingTarget = scenePaths.TakeWhile((x,i)=>!EditorUtility.DisplayCancelableProgressBar("Search component in scenes", x, i * progressWeight))
												.Where(x=>hasComponentInScenePaths(x, componentType))
												.Select(x=>AssetDatabase.LoadAssetAtPath(x, typeof(Object)))
												.ToArray();
			createEmptyScene(true);
			EditorUtility.ClearProgressBar();
		}
	}

	/// <summary>
	/// シーン内のコンポーネント格納確認
	/// </summary>
	/// <param name="path">シーンパス</param>
	/// <param name="componentType">検索コンポーネント型</param>
	/// <returns>true:格納している、false:格納していない</returns>
	/// <remarks>シーンを破壊ので注意</remarks>
	private bool hasComponentInScenePaths(string path, System.Type componentType) {
		createEmptyScene(true);
		EditorApplication.OpenScene(path);
		return getTopGameObjectsInScene().Any(x=>0 < x.GetComponentsInChildren(componentType, true).Length);
	}

	/// <summary>
	/// リソース検索
	/// </summary>
	/// <param name="resource">検索リソース</param>
	private void searchResource(Object resource) {
		reset();
		if ((mLookIn & LookIn.Prefab) != 0) {
			searchResourceInPrefabs(resource);
		}
		if ((mLookIn & LookIn.Scene) != 0) {
			searchResourceInScenes(resource);
		}
	}

	/// <summary>
	/// プレファブ内リソース検索
	/// </summary>
	/// <param name="resource">検索リソース</param>
	private void searchResourceInPrefabs(Object resource) {
		if (createEmptyScene(false)) {
			var prefabPaths = getAllPrefabPaths();
			var progressWeight = 1.0f / prefabPaths.Count();
			mPrefabsPreservingTarget = prefabPaths.TakeWhile((x,i)=>!EditorUtility.DisplayCancelableProgressBar("Search component in prefabs", x, i * progressWeight))
													.Where(x=>hasResourceInPrefabPath(x, resource))
													.Select(x=>(GameObject)AssetDatabase.LoadAssetAtPath(x, typeof(GameObject)))
													.ToArray();
			createEmptyScene(true);
			EditorUtility.ClearProgressBar();
		}
	}

	/// <summary>
	/// プレファブ内のリソース格納確認
	/// </summary>
	/// <param name="path">プレファブパス</param>
	/// <param name="resource">検索リソース</param>
	/// <returns>true:格納している、false:格納していない</returns>
	/// <remarks>シーンを汚すので注意</remarks>
	private bool hasResourceInPrefabPath(string path, Object resource) {
		var gc = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath(path, typeof(GameObject)));
		var result = hasResourceInGameObject(gc, resource);
		DestroyImmediate(gc);
		return result;
	}

	/// <summary>
	/// シーン内リソース検索
	/// </summary>
	/// <param name="resource">検索リソース</param>
	private void searchResourceInScenes(Object resource) {
		if (createEmptyScene(false)) {
			var scenePaths = getAllScenePaths();
			var progressWeight = 1.0f / scenePaths.Count();
			mScenesPreservingTarget = scenePaths.TakeWhile((x,i)=>!EditorUtility.DisplayCancelableProgressBar("Search component in scenes", x, i * progressWeight))
												.Where(x=>hasResourceInScenePaths(x, resource))
												.Select(x=>AssetDatabase.LoadAssetAtPath(x, typeof(Object)))
												.ToArray();
			createEmptyScene(true);
			EditorUtility.ClearProgressBar();
		}
	}

	/// <summary>
	/// シーン内のリソース格納確認
	/// </summary>
	/// <param name="path">シーンパス</param>
	/// <param name="resource">検索リソース</param>
	/// <returns>true:格納している、false:格納していない</returns>
	/// <remarks>シーンを破壊ので注意</remarks>
	private bool hasResourceInScenePaths(string path, Object resource) {
		createEmptyScene(true);
		EditorApplication.OpenScene(path);
		return getTopGameObjectsInScene().Any(x=>hasResourceInGameObject(x, resource));
	}

	/// <summary>
	/// ゲームオブジェクト内のリソース格納確認
	/// </summary>
	/// <param name="go">ゲームオブジェクト</param>
	/// <param name="resource">検索リソース</param>
	/// <returns>true:格納している、false:格納していない</returns>
	private static bool hasResourceInGameObject(GameObject go, Object resource) {
		List<Object> objects = new List<Object>();
		objects.AddRange(go.GetComponentsInChildren<Component>(true));
		var checkedObjects = objects.ToDictionary(x=>x.GetInstanceID(), x=>(object)null);
		while (0 < objects.Count) {
			var sp = new SerializedObject(objects[0]).GetIterator();
			while (sp.Next(true)) {
				var isValid = true;
				isValid = isValid && (sp.propertyType == SerializedPropertyType.ObjectReference);			//Object型である
				isValid = isValid && (sp.objectReferenceValue != null);									//nullでない
				if (isValid) {
					//対象容疑オブジェクト
					if (sp.objectReferenceValue == resource) {
						//対象なら
						return true;
					} else {
						//対象で無いなら
						var isChild = true;
						isChild = isChild && (sp.propertyPath != "m_PrefabParentObject");							//オリジナルプレファブへのパスでない
						isChild = isChild && (!checkedObjects.ContainsKey(sp.objectReferenceInstanceIDValue));	//まだ検証されていない
						if (isChild) {
							//中を追加検索
							objects.Add(sp.objectReferenceValue);
							checkedObjects.Add(sp.objectReferenceInstanceIDValue, null);
						}
					}
				}
			}
			objects.RemoveAt(0);
		}
		return false;
	}

	/// <summary>
	/// 全ての型を取得する
	/// </summary>
	/// <returns>全ての型</returns>
	private static IEnumerable<System.Type> getAllTypes() {
		return System.AppDomain.CurrentDomain.GetAssemblies().SelectMany(x=>x.GetTypes());
	}

	/// <summary>
	/// 全てのコンポーネント型を取得する
	/// </summary>
	/// <returns>全てのコンポーネント型</returns>
	private static IEnumerable<System.Type> getAllComponentTypes() {
		var componentType = typeof(MonoBehaviour);
		return getAllTypes().Where(x=>x.IsSubclassOf(componentType));
	}

	/// <summary>
	/// 指定されたコンポーネント型を取得する
	/// </summary>
	/// <param name="value">検索文字列</param>
	/// <returns>指定されたコンポーネント型</returns>
	private IEnumerable<System.Type> getComponentTypes(string indexOf) {
		return mComponentTypes.Where(x=>0 <= x.FullName.IndexOf(indexOf, System.StringComparison.CurrentCultureIgnoreCase));
	}

	/// <summary>
	/// 指定されたコンポーネント型を取得する
	/// </summary>
	/// <param name="pattern">パスフィルタ正規表現</param>
	/// <returns>指定されたコンポーネント型</returns>
	private IEnumerable<System.Type> getComponentTypes(Regex pattern) {
		return mComponentTypes.Where(x=>pattern.Match(x.FullName).Success);
	}

	/// <summary>
	/// 全アセットのパスを取得する
	/// </summary>
	/// <returns>全アセットのパス</returns>
	private static IEnumerable<string> getAllAssetPaths() {
		return AssetDatabase.GetAllAssetPaths();
	}

	/// <summary>
	/// 全プレファブのパスを取得する
	/// </summary>
	/// <returns>全プレファブのパス</returns>
	private static IEnumerable<string> getAllPrefabPaths() {
		return getAllAssetPaths().Where(x=>x.EndsWith(".prefab"));
	}

	/// <summary>
	/// 全シーンのパスを取得する
	/// </summary>
	/// <returns>全シーンのパス</returns>
	private static IEnumerable<string> getAllScenePaths() {
		return getAllAssetPaths().Where(x=>x.EndsWith(".unity"));
	}

	/// <summary>
	/// シーン内の全てのゲームオブジェクトを取得する
	/// </summary>
	/// <returns>シーン内の全てのゲームオブジェクト</returns>
	private static IEnumerable<GameObject> getAllGameObjectsInScene() {
		IEnumerable<GameObject> result;
		if (string.IsNullOrEmpty(EditorApplication.currentScene)) {
			//シーン名が無いなら
			var oldObjects = Selection.objects;
			Selection.objects = Resources.FindObjectsOfTypeAll<GameObject>();
			var results = Selection.GetFiltered(typeof(GameObject), SelectionMode.ExcludePrefab)
										.Select(x=>(GameObject)x)
										.ToArray();
			result = results;
			Selection.objects = oldObjects;
		} else {
			//シーン名が有るなら
			result = Resources.FindObjectsOfTypeAll<GameObject>()
								.Where(x=>AssetDatabase.GetAssetOrScenePath(x) == EditorApplication.currentScene);
		}
		return result;
	}

	/// <summary>
	/// シーン内のトップゲームオブジェクトを取得する
	/// </summary>
	/// <returns>シーン内のトップゲームオブジェクト</returns>
	private static IEnumerable<GameObject> getTopGameObjectsInScene() {
		return getAllGameObjectsInScene().Where(x=>x.transform.parent == null);
	}
}
