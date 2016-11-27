using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Text;

namespace SearchTools {
	public class LinkAnalyzer : System.IDisposable {

		public struct AssetUniqueID {
			public string guid;
			public int fileID;

			public AssetUniqueID(string guid, int fileID) {
				this.guid = guid;
				this.fileID = fileID;
			}
			public AssetUniqueID(string guid) : this(guid, 0) {
			}

			public static AssetUniqueID none {get{
				return new AssetUniqueID(string.Empty, 0);
			}}

			public override bool Equals(object other) {
				if (other is AssetUniqueID) {
					var p = (AssetUniqueID)other;
					return (guid == p.guid) && (fileID == p.fileID);
				}
				return false;
			}

			public override int GetHashCode() {
				var fileIDHashCode = fileID.GetHashCode();
				var result = (fileIDHashCode << 17) ^ (int)((uint)fileIDHashCode >> 15);
				result ^= ((guid != null)? guid.GetHashCode(): unchecked((int)0xF0F0F0F0));
				return  result;
			}

			public override string ToString() {
				string result;
				if (fileID == 0) {
					result = guid;
				} else {
					var sb = new StringBuilder(44);
					sb.Append(guid);
					sb.Append(":");
					sb.Append(fileID);
					result = sb.ToString();
				}
				return result;
			}
		}

		/// <summary>
		/// IsInclude戻り値
		/// </summary>
		public enum IsIncludeReturn {
			False,
			True,
			Unknown,
		}

		/// <summary>
		/// 梱包情報
		/// </summary>
		[System.Flags]
		public enum IncludeStateFlags { 
			NonInclude				= 1<<0,
			Link					= 1<<1,
			Scripts					= 1<<2,
			Resources				= 1<<3,
			ScenesInBuild			= 1<<4,
			AlwaysIncludedShaders	= 1<<5,
		}

		/// <summary>
		/// 解析中確認
		/// </summary>
		public  bool analyzing {get{
			return analyzeThread != null;
		}}

		/// <summary>
		/// 解析スレッド
		/// </summary>
		public  float progress {get{
			return ((analyzing)? Mathf.Min(analyzeProgress, 1.0f - float.Epsilon): analyzeProgress);
		}}

		/// <summary>
		/// IDisposableインターフェース
		/// </summary>
		public void Dispose() {
			if(analyzeThread != null) {
				analyzeThread.Abort(); 
			}
		}

		/// <summary>
		/// オブジェクトをユニークIDに変換
		/// </summary>
		public static AssetUniqueID ConvertObjectToUniqueID(Object obj) {
			var assetPath = AssetDatabase.GetAssetPath(obj);
			var guid = AssetDatabase.AssetPathToGUID(assetPath);
			var result = new AssetUniqueID(guid);

			bool hasFileID;
			var linkerType = GetLinkerType(assetPath);
			switch (linkerType) {
			case GetLinkerTypeReturn.Home:
			case GetLinkerTypeReturn.MetaHome:
			case GetLinkerTypeReturn.Script:
				hasFileID = false;
				break;
			case GetLinkerTypeReturn.Importer:
				hasFileID = AssetDatabase.IsSubAsset(obj);
				break;
			default:
				hasFileID = true;
				break;
			}
			if (hasFileID) {
				var instanceID = obj.GetInstanceID();
				var fileID = Unsupported.GetLocalIdentifierInFile(instanceID);
				result.fileID = fileID;
			}
			return result;
		}

		/// <summary>
		/// アセットユニークID確認
		/// </summary>
		public static bool IsAsset(AssetUniqueID uniqueID) {
			var result = false;
			if (!string.IsNullOrEmpty(uniqueID.guid)) {
				var match = guidMatchRegex.Match("guid: " + uniqueID.guid);
				result = match.Success;
			}
			return result;
		}

		/// <summary>
		/// スプライトパッキングユニークID確認
		/// </summary>
		public static bool IsSpritePackingTag(AssetUniqueID uniqueID) {
			var result = false;
			if (!string.IsNullOrEmpty(uniqueID.guid)) {
				result = uniqueID.guid.StartsWith(spritePackingTagsPrefix);
				result = result && (uniqueID.fileID == 0);
			}
			return result;
		}

		/// <summary>
		/// ユニークIDの梱包確認
		/// </summary>
		public IsIncludeReturn IsInclude(AssetUniqueID uniqueID) {
			var result = IsIncludeReturn.Unknown;
			if (analyzeData.ContainsKey(uniqueID)) {
				var state = analyzeData[uniqueID].state;
				switch (state) { 
				case 0:
					//empty.
					break;
				case IncludeStateFlags.NonInclude:
					result = IsIncludeReturn.False;
					break;
				default:
					result = IsIncludeReturn.True;
					break;
				}
			}
			return result;
		}

		/// <summary>
		/// パスの梱包確認
		/// </summary>
		public IsIncludeReturn IsIncludeFromPath(string path) {
			var result = IsIncludeReturn.Unknown;
			if (pathToGuid.ContainsKey(path)) {
				var uniqueID = new AssetUniqueID(pathToGuid[path]);
				result = IsInclude(uniqueID);
			}
			return result;
		}

		/// <summary>
		/// ユニークIDの梱包情報取得
		/// </summary>
		public IncludeStateFlags GetIncludeStateFlags(AssetUniqueID uniqueID) {
			IncludeStateFlags result = 0;
			if (analyzeData.ContainsKey(uniqueID)) {
				result = analyzeData[uniqueID].state;
			}
			return result;
		}

		/// <summary>
		/// パスの梱包情報取得
		/// </summary>
		public IncludeStateFlags GetIncludeStateFlags(string path) {
			IncludeStateFlags result = 0;
			if (pathToGuid.ContainsKey(path)) {
				var uniqueID = new AssetUniqueID(pathToGuid[path]);
				result = GetIncludeStateFlags(uniqueID);
			}
			return result;
		}

		/// <summary>
		/// スプライトパッキングタグの梱包確認
		/// </summary>
		public IsIncludeReturn IsIncludeFromSpritePackingTag(string tag) {
			return IsInclude(ConvertSpritePackingTagToUniqueID(tag));
		}

		/// <summary>
		/// リンク取得
		/// </summary>
		public List<AssetUniqueID> GetLinks(AssetUniqueID uniqueID) {
			List<AssetUniqueID> result = null;
			if (analyzeData.ContainsKey(uniqueID) && (analyzeData[uniqueID].state != 0)) {
				result = analyzeData[uniqueID].links;
			} else if (uniqueID.fileID != 0) {
				uniqueID.fileID = 0;
				result = GetLinks(uniqueID);
			}
			return result;
		}

		/// <summary>
		/// リンク取得
		/// </summary>
		public List<AssetUniqueID> GetLinks(string path) {
			List<AssetUniqueID> result = null;
			if (pathToGuid.ContainsKey(path)) {
				var guid = pathToGuid[path];
				var uniqueID = new AssetUniqueID(guid);
				result = GetLinks(uniqueID);
			}
			return result;
		}

		/// <summary>
		/// 逆リンク取得
		/// </summary>
		public List<AssetUniqueID> GetInboundLinks(AssetUniqueID uniqueID) {
			List<AssetUniqueID> result = null;
			if (!analyzing && analyzeData.ContainsKey(uniqueID) && (analyzeData[uniqueID].state != 0)) {
				result = analyzeData[uniqueID].inboundLinks;
			} else if (uniqueID.fileID != 0) {
				uniqueID.fileID = 0;
				result = GetInboundLinks(uniqueID);
			}
			return result;
		}

		/// <summary>
		/// 逆リンク取得
		/// </summary>
		public List<AssetUniqueID> GetInboundLinks(string path) {
			List<AssetUniqueID> result = null;
			if (pathToGuid.ContainsKey(path)) {
				var guid = pathToGuid[path];
				var uniqueID = new AssetUniqueID(guid);
				result = GetInboundLinks(uniqueID);
			}
			return result;
		}

		/// <summary>
		/// パスからスプライトパッキングタグ取得
		/// </summary>
		public string GetSpritePackingTag(AssetUniqueID uniqueID) {
			string result = null;
			if (!analyzing && analyzeData.ContainsKey(uniqueID) && (analyzeData[uniqueID].state != 0)) {
				result = analyzeData[uniqueID].spritePackingTag;
			} else if (uniqueID.fileID != 0) {
				uniqueID.fileID = 0;
				result = GetSpritePackingTag(uniqueID);
			}
			return result;
		}

		/// <summary>
		/// パスからスプライトパッキングタグ取得
		/// </summary>
		public string GetSpritePackingTag(string path) {
			return GetSpritePackingTag(ConvertSpritePackingTagToUniqueID(path));
		}

		/// <summary>
		/// SpritePackingTagをユニークIDに変換
		/// </summary>
		public static AssetUniqueID ConvertSpritePackingTagToUniqueID(string spritePackingTag) {
			return new AssetUniqueID(spritePackingTagsPrefix + spritePackingTag, 0);
		}

		/// <summary>
		/// SpritePackingTagをユニークIDに変換
		/// </summary>
		public static string ConvertUniqueIDToSpritePackingTag(AssetUniqueID uniqueID) {
			string result = null;
			if (IsSpritePackingTag(uniqueID)) { 
				result = uniqueID.guid.Substring(spritePackingTagsPrefix.Length);
			}
			return result;
		}

		/// <summary>
		/// 開始
		/// </summary>
		public void Start() {
			if (analyzeProgress == 0.0f) {
				var allAssetPaths = AssetDatabase.GetAllAssetPaths();

				includeScenePaths = EditorBuildSettings.scenes.Where(x=>x.enabled)
														.Select(x=>x.path)
														.ToArray();
				if (includeScenePaths.Length == 0) {
					var activeScenePath = EditorSceneManager.GetActiveScene().path;
					if (!string.IsNullOrEmpty(activeScenePath)) {
						includeScenePaths = new[]{activeScenePath};
					}
				}

				analyzeData = new Dictionary<AssetUniqueID, AssetInfo>(allAssetPaths.Length);
				guidToPath = new Dictionary<string, string>(allAssetPaths.Length);
				pathToGuid = new Dictionary<string, string>(allAssetPaths.Length);
				foreach (var path in allAssetPaths) {
					if (path.StartsWith(assetsPrefix)) {
						var guid = AssetDatabase.AssetPathToGUID(path);
						guidToPath.Add(guid, path);
						pathToGuid.Add(path, guid);
					}
				}

#if SEARCH_TOOLS_DEBUG
				Analyze();
#else
				analyzeThread = new Thread(Analyze);
				analyzeThread.Start();
#endif
			}
		}

		/// <summary>
		/// リフレッシュ
		/// </summary>
		public void Refresh() {
			if(analyzeThread != null) {
				analyzeThread.Abort(); 
			}
			analyzeProgress = 0.0f;
			Start();
		}

		/// <summary>
		/// 解析スレッド
		/// </summary>
		private Thread analyzeThread = null;

		/// <summary>
		/// 解析進捗
		/// </summary>
		private float analyzeProgress = 0.0f;

		/// <summary>
		/// 解析パス
		/// </summary>
		private static readonly string dataBasePath = Application.dataPath.Substring(0, Application.dataPath.Length - 6); //末端の"Assets"を削除

		/// <summary>
		/// サブアセットマッチング正規表現
		/// </summary>
		private static readonly Regex subAssetMatchRegex = new Regex(@"--- !u![1-9][0-9]* &([1-9][0-9]*)");

		/// <summary>
		/// GUIDマッチング正規表現
		/// </summary>
		private static readonly Regex guidMatchRegex = new Regex(@"guid:[\s&&[^\r\n]]*([0-9a-zA-Z]{32})");

		/// <summary>
		/// fileIDマッチング正規表現
		/// </summary>
		private static readonly Regex fileIDMatchRegex = new Regex(@"fileID:[\s&&[^\r\n]]*([1-9][0-9]*)");

		/// <summary>
		/// SpritePackingTagマッチング正規表現
		/// </summary>
		private static readonly Regex spritePackingTagMatchRegex = new Regex(@"spritePackingTag:[\s&&[^\r\n]]*(.+)");

		/// <summary>
		/// fileIDToRecycleNameルートマッチング正規表現
		/// </summary>
		private static readonly Regex fileIDToRecycleNameRootMatchRegex = new Regex(@"fileIDToRecycleName:");

		/// <summary>
		/// fileIDToRecycleNameノードマッチング正規表現
		/// </summary>
		private static readonly Regex fileIDToRecycleNameNodeMatchRegex = new Regex(@"([1-9][0-9]*):[\s&&[^\r\n]]*.+");

		/// <summary>
		/// リンク情報
		/// </summary>
		private struct LinkInfo {
			public List<AssetUniqueID> links;
			public string spritePackingTag;
		}

		/// <summary>
		/// アセット情報
		/// </summary>
		private class AssetInfo {
			public IncludeStateFlags state;
			public LinkInfo linkInfo;
			public List<AssetUniqueID> links {get{return linkInfo.links;} set{linkInfo.links = value;}}
			public List<AssetUniqueID> inboundLinks;
			public string spritePackingTag {get{return linkInfo.spritePackingTag;} set{linkInfo.spritePackingTag = value;}}

			public AssetInfo() {
				state = 0;
				linkInfo = new LinkInfo(){links = null, spritePackingTag = null};
				inboundLinks = null;
			}
			public AssetInfo(IncludeStateFlags state, List<AssetUniqueID> links, string spritePackingTag) {
				this.state = state;
				linkInfo = new LinkInfo(){links = links, spritePackingTag = spritePackingTag};
				inboundLinks = null;
			}
		}

		/// <summary>
		/// アセットパスのプレフィックス
		/// </summary>
		private const string assetsPrefix = "Assets/";

		/// <summary>
		/// SpritePackingTagsパスのプレフィックス
		/// </summary>
		private const string spritePackingTagsPrefix = "SpritePackingTags/";

		/// <summary>
		/// 解析結果
		/// </summary>
		private Dictionary<AssetUniqueID, AssetInfo> analyzeData = null;

		/// <summary>
		/// 梱包シーンパス
		/// </summary>
		private string[] includeScenePaths = null;

		/// <summary>
		/// GUIDパス変換辞書
		/// </summary>
		private Dictionary<string, string> guidToPath = null;

		/// <summary>
		/// パスメインユニークID変換辞書
		/// </summary>
		private Dictionary<string, string> pathToGuid = null;

		/// <summary>
		/// 解析開始
		/// </summary>
		private void Analyze() {
			//スクリプト梱包判定
			var scriptsCount = AnalyzeForScript();

			var progressUnit = 1.0f / (pathToGuid.Count - scriptsCount + 2);
			var doneCount = 0.0f;

			//アセット梱包判定
			AnalyzeForAsset(ref doneCount, progressUnit);

			//残りを除外判定
			ExcludeForLeftovers(ref doneCount, progressUnit);

			//fileIDの正規化
			FileIDNormalize(ref doneCount, progressUnit);

			//逆リンク判定
			AnalyzeForInboundLink(ref doneCount, progressUnit);

			analyzeThread = null;
		}

		/// <summary>
		/// スクリプト梱包判定
		/// </summary>
		private int AnalyzeForScript() {
			var result = 0;
			foreach (var ptg in pathToGuid) {
				var path = ptg.Key;

				var linkerType = GetLinkerType(path);
				switch (linkerType) {
				case GetLinkerTypeReturn.Script:
					{
						var guid = ptg.Value;
						var dat = new AssetInfo();
						analyzeData.Add(new AssetUniqueID(guid), dat);

						if (-1 == path.IndexOf("/Editor/")) {
							dat.state = IncludeStateFlags.Scripts;
						} else {
							dat.state = IncludeStateFlags.NonInclude;
						}
						++result;
					}
					break;
				default:
					//empty.
					break;
				}
			}
			return result;
		}

		/// <summary>
		/// アセット梱包判定
		/// </summary>
		private void AnalyzeForAsset(ref float doneCount, float progressUnit) {
			var analyzeQueue = new Queue<string>();

			//信頼されたルートの検索
			var trustedRootPaths = GetTrustedRootPaths();
			foreach (var trustedRootPath in trustedRootPaths.Keys) {
				analyzeQueue.Enqueue(trustedRootPath);
			}

			while (0 < analyzeQueue.Count) {
				var analyzePath = analyzeQueue.Dequeue();
				var analyzeUniqueID = new AssetUniqueID(pathToGuid[analyzePath]);

				if (!analyzeData.ContainsKey(analyzeUniqueID) || (analyzeData[analyzeUniqueID].state == 0)) {
					var linkInfos = GetLinkUniqueIDsFromAssetPath(analyzePath);
					foreach (var linkInfo in linkInfos) {
						if (!analyzeData.ContainsKey(linkInfo.Key)) {
							analyzeData.Add(linkInfo.Key, new AssetInfo());
						}
						var dat = analyzeData[linkInfo.Key];
						dat.linkInfo = linkInfo.Value;
						if ((dat.links != null)) {
							foreach (var link in dat.links.Where(x=>x.guid != analyzeUniqueID.guid)) {
								analyzeQueue.Enqueue(guidToPath[link.guid]);
							}
						}
						if (!string.IsNullOrEmpty(dat.spritePackingTag)) {
							var spritePackingTagUniqueID = ConvertSpritePackingTagToUniqueID(dat.spritePackingTag);
							if (!analyzeData.ContainsKey(spritePackingTagUniqueID)) {
								analyzeData.Add(spritePackingTagUniqueID, new AssetInfo(IncludeStateFlags.Link, new List<AssetUniqueID>(), null));
							}
							analyzeData[spritePackingTagUniqueID].links.Add(analyzeUniqueID);
						}

						if (trustedRootPaths.ContainsKey(analyzePath)) {
							dat.state = trustedRootPaths[analyzePath];
						} else {
							dat.state = IncludeStateFlags.Link;
						}
					}

					++doneCount;
					analyzeProgress = doneCount * progressUnit;
				}
			}
		}

		/// <summary>
		/// 信頼されたルートパスを取得
		/// </summary>
		private Dictionary<string, IncludeStateFlags> GetTrustedRootPaths() {
			var result = new Dictionary<string, IncludeStateFlags>();

			//Scenes In Build
			foreach (var includeScenePath in includeScenePaths) {
				if (result.ContainsKey(includeScenePath)) { 
					result[includeScenePath] |= IncludeStateFlags.ScenesInBuild;
				} else { 
					result.Add(includeScenePath, IncludeStateFlags.ScenesInBuild);
				}
			}

			//Always Included Shaders
			foreach (var uniqueID in GetLinkUniqueIDsFromAssetPath("ProjectSettings/GraphicsSettings.asset").Values.Where(x=>x.links != null).SelectMany(x=>x.links)) {
				if (guidToPath.ContainsKey(uniqueID.guid)) {
					var path = guidToPath[uniqueID.guid];
					if (result.ContainsKey(path)) { 
						result[path] |= IncludeStateFlags.AlwaysIncludedShaders;
					} else { 
						result.Add(path, IncludeStateFlags.AlwaysIncludedShaders);
					}
				}
			}

			//Resources
			foreach (var path in pathToGuid.Keys) {
				if (0 <= path.IndexOf("/Resources/")) {
					if (result.ContainsKey(path)) { 
						result[path] |= IncludeStateFlags.Resources;
					} else { 
						result.Add(path, IncludeStateFlags.Resources);
					}
				}
			}

			return result;
		}

		/// <summary>
		/// アセットからリンクパスを取得
		/// </summary>
		private Dictionary<AssetUniqueID, LinkInfo> GetLinkUniqueIDsFromAssetPath(string path) {
			var result = new Dictionary<AssetUniqueID, LinkInfo>();

			List<string> text;
			var linkerType = GetLinkerType(path);
			switch (linkerType) {
			case GetLinkerTypeReturn.Home:
			case GetLinkerTypeReturn.Apartment:
				text = readTextFromAsset(path);
				break;
			case GetLinkerTypeReturn.MetaHome:
			case GetLinkerTypeReturn.MetaApartment:
			case GetLinkerTypeReturn.Importer:
				text = readTextFromAsset(path + ".meta");
				break;
			case GetLinkerTypeReturn.Unknown:
			default:
				text = null;
				break;
			}
			if ((text == null) || (text.Count == 0)) {
				return result;
			}

			var linkInfo = new LinkInfo();
			var uniqueID = new AssetUniqueID();
			if (pathToGuid.ContainsKey(path)) {
				uniqueID.guid = pathToGuid[path];
			}
			foreach (var line in text) {
				//SubAsset確認
				switch (linkerType) {
				case GetLinkerTypeReturn.Home:
				case GetLinkerTypeReturn.MetaHome:
				case GetLinkerTypeReturn.Importer:
					//empty.
					break;
				default:
					{
						var subAssetMatch = subAssetMatchRegex.Match(line);
						int fileID;
						if (subAssetMatch.Success && int.TryParse(subAssetMatch.Groups[1].Value, out fileID)) {
							if (uniqueID.fileID != fileID) {
								//サブアセット区切りなら
								//解析分を現サブアセットに反映
								result.Add(uniqueID, linkInfo);
								//次サブアセットを新規作成
								uniqueID.fileID = fileID;
								linkInfo.links = null;
								linkInfo.spritePackingTag = null;
							}
						}
					}
					break;
				}

				//リンクGUID列挙
				do {
					var fileIDMatch = fileIDMatchRegex.Match(line);
					var guidMatch = guidMatchRegex.Match(line);
					if (fileIDMatch.Success) {
						int fileID = 0;
						int.TryParse(fileIDMatch.Groups[1].Value, out fileID);

						string guid;
						if (guidMatch.Success) {
							guid = guidMatch.Groups[1].Value;
						} else { 
							guid = uniqueID.guid;
						}
						if (!guidToPath.ContainsKey(guid)) {
							break;
						}

						if ((uniqueID.guid != guid) || (uniqueID.fileID != fileID)) {
							if (linkInfo.links == null) {
								linkInfo.links = new List<AssetUniqueID>();
							}
							var linkUniqueID = new AssetUniqueID(guid, fileID);
							linkInfo.links.Add(linkUniqueID);
						}
					}
				} while(false);

				//SpritePackingTag取得
				if (linkerType == GetLinkerTypeReturn.Importer) {
					var match = spritePackingTagMatchRegex.Match(line);
					if (match.Success) {
						var spritePackingTag = match.Groups[1].Value;
						if (spritePackingTag[0] == '\'') {
							spritePackingTag = spritePackingTag.Substring(1, spritePackingTag.Length - 2).Replace("''", "'");
						}
						linkInfo.spritePackingTag = spritePackingTag;
					}
				}
			}
			//Importer系LinkerのfileID解析
			if (linkerType == GetLinkerTypeReturn.Importer) {
				var i = 0;
				var iMax = text.Count;
				while (i < iMax) {
					var line = text[i++];
					var match = fileIDToRecycleNameRootMatchRegex.Match(line);
					if (match.Success) {
						break;
					}
				}
				while (i < iMax) {
					var line = text[i++];
					var match = fileIDToRecycleNameNodeMatchRegex.Match(line);
					if (!match.Success) {
						break;
					}
					int fileID;
					if (int.TryParse(match.Groups[1].Value, out fileID)) {
						var nodeUniqueId = new AssetUniqueID(uniqueID.guid, fileID);
						var nodeLinkInfo = new LinkInfo(){links = new List<AssetUniqueID>(){uniqueID}}; //Importer系なら無条件で親を参照
						result.Add(nodeUniqueId, nodeLinkInfo);
					}
				}
			}
			result.Add(uniqueID, linkInfo);

			//重複リンク削除
			foreach (var key in result.Keys) {
				var links = result[key].links;
				if (links != null) {
					links.Sort((x,y)=>{
						var compare = string.Compare(x.guid, y.guid);
						if (compare == 0) {
							compare = x.fileID - y.fileID;
						}
						return compare;
					});
					for (var i = links.Count - 2; 0 <= i; --i) {
						if ((links[i].guid == links[i + 1].guid) && (links[i].fileID == links[i + 1].fileID)) {
							links.RemoveAt(i + 1);
						}
					}
				}
			}

			return result;
		}

		/// <summary>
		/// GetLinkerType戻り値
		/// </summary>
		private enum GetLinkerTypeReturn {
			Unknown,
			Home,
			MetaHome,
			Apartment,
			MetaApartment,
			Importer,
			Script,
		}

		/// <summary>
		/// リンカー確認
		/// </summary>
		private static GetLinkerTypeReturn GetLinkerType(string path) {
			var result = GetLinkerTypeReturn.Unknown;

			var extStartIndex = path.LastIndexOf('.');
			if (extStartIndex < 0) {
				extStartIndex = path.Length;
			}
			var ext = path.Substring(extStartIndex, path.Length - extStartIndex);
			switch (ext) {
			case ".prefab":
			case ".unity":
				result = GetLinkerTypeReturn.Home;
				break;
			case ".anim":
			case ".asset":
			case ".controller":
			case ".cubemap":
			case ".flare":
			case ".fontsettings":
			case ".guiskin":
			case ".mask":
			case ".mat":
			case ".mixer":
			case ".overrideController":
				result = GetLinkerTypeReturn.Apartment;
				break;
			case ".giparams":
			case ".physicMaterial":
			case ".physicsMaterial2D":
			case ".renderTexture":
				result = GetLinkerTypeReturn.Home;
				break;
			case ".shader":
			case ".txt":
				result = GetLinkerTypeReturn.MetaHome;
				break;
			case ".fbx":
				result = GetLinkerTypeReturn.Importer;
				break;
			case ".bmp":
			case ".gif":
			case ".iff":
			case ".jpg": case ".jpeg":
			case ".pic": case ".pict":
			case ".png":
			case ".psd":
			case ".tif": case ".tiff":
			case ".tga":
				result = GetLinkerTypeReturn.Importer;
				break;
			case ".wav":
				result = GetLinkerTypeReturn.MetaApartment;
				break;
			case ".cs":
			case ".js":
				result = GetLinkerTypeReturn.Script;
				break;
			default:
				if (IsDirectory(path)) {
					result = GetLinkerTypeReturn.MetaHome;
				} else {
#if SEARCH_TOOLS_DEBUG
					Debug.Log("Unknown linker type:" + ext + ":" + path);
#endif
					result = GetLinkerTypeReturn.MetaApartment;
				}
				break;
			}
			return result;
		}

		/// <summary>
		/// ディレクトリ確認
		/// </summary>
		private static bool IsDirectory(string path) {
			var fullPath = dataBasePath + path;
			return Directory.Exists(fullPath);
		}

		/// <summary>
		/// アセットからのテキスト読み込み
		/// </summary>
		private List<string> readTextFromAsset(string path) {
			var result = new List<string>();
			try {
				using (var sr = new StreamReader(dataBasePath + path)) {
					while (!sr.EndOfStream) {
						result.Add(sr.ReadLine());
					}
					sr.Close();
				}
			} catch (System.UnauthorizedAccessException) {
				//empty.
			} catch (FileNotFoundException) {
				//empty.
			} catch (System.Exception e) {
				Debug.Log(e);
			}
			return result;
		}

		/// <summary>
		/// LoadAllAssetsAtPath()の使用可能確認
		/// </summary>
		private static bool CanUseLoadAllAssetsAtPath(string path) {
			return !path.EndsWith(".unity");
		}

		/// <summary>
		/// 残りを除外判定
		/// </summary>
		private void ExcludeForLeftovers(ref float doneCount, float progressUnit) {
			foreach(var analyzePath in pathToGuid.Keys) {
				var analyzeUniqueID = new AssetUniqueID(pathToGuid[analyzePath]);
				if (!analyzeData.ContainsKey(analyzeUniqueID) || analyzeData[analyzeUniqueID].state == 0) {
					var linkInfos = GetLinkUniqueIDsFromAssetPath(analyzePath);
					foreach (var linkInfo in linkInfos) {
						if (!analyzeData.ContainsKey(linkInfo.Key)) {
							analyzeData.Add(linkInfo.Key, new AssetInfo());
						}
						var dat = analyzeData[linkInfo.Key];
						IncludeStateFlags datState = 0;
						dat.linkInfo = linkInfo.Value;
						if (!string.IsNullOrEmpty(dat.spritePackingTag)) {
							var spritePackingTagUniqueID = ConvertSpritePackingTagToUniqueID(dat.spritePackingTag);
							if (!analyzeData.ContainsKey(spritePackingTagUniqueID)) {
								analyzeData.Add(spritePackingTagUniqueID, new AssetInfo(0, new List<AssetUniqueID>(), null));
							}
							analyzeData[spritePackingTagUniqueID].links.Add(analyzeUniqueID);

							datState = analyzeData[spritePackingTagUniqueID].state;
						}
						if (datState == 0) { 
							datState = IncludeStateFlags.NonInclude;
						}
						dat.state = datState;
					}

					++doneCount;
					analyzeProgress = doneCount * progressUnit;
				}
			}
		}

		/// <summary>
		/// fileIDの正規化
		/// </summary>
		private void FileIDNormalize(ref float doneCount, float progressUnit) {
			foreach(var dat in analyzeData) {
				var assetInfo = dat.Value;
				if (assetInfo.links != null) {
					var uniqueID = dat.Key;
					var links = assetInfo.links;
					for(int i = 0, iMax = links.Count; i < iMax; ++i) {
						if ((links[i].fileID != 0) && !analyzeData.ContainsKey(links[i])) {
							var normalizedLink = new AssetUniqueID(links[i].guid);
							if ((uniqueID.guid == normalizedLink.guid) && (uniqueID.fileID == normalizedLink.fileID)) {
								links.RemoveAt(i);
								--i;
								--iMax;
								continue;
							} else if (analyzeData.ContainsKey(normalizedLink)) {
								links[i] = normalizedLink;
							}
							if (0 < i) {
								if ((links[i].guid == links[i - 1].guid) && (links[i].fileID == links[i - 1].fileID)) {
									links.RemoveAt(i);
									--i;
									--iMax;
								}
							}
						}
					}
					links.Sort((x,y)=>{
						var compare = string.Compare(guidToPath[x.guid], guidToPath[y.guid]);
						if (compare == 0) {
							compare = x.fileID - y.fileID;
						}
						return compare;
					});
				}
			}

			++doneCount;
			analyzeProgress = doneCount * progressUnit;
		}

		/// <summary>
		/// 逆リンク判定
		/// </summary>
		private void AnalyzeForInboundLink(ref float doneCount, float progressUnit) {
			foreach(var dat in analyzeData) {
				if (IsSpritePackingTag(dat.Key)) {
					continue;
				}
				if (dat.Value.links != null) {
#if SEARCH_TOOLS_DEBUG
					foreach(var link2 in dat.Value.links) {
						var link = link2;
						if (!analyzeData.ContainsKey(link)) {
							var l = new AssetUniqueID(link.guid, 0);
							var noFileID = analyzeData.ContainsKey(l);
							Debug.Log(link + ":" + ((noFileID)? "O": "X") + ":    " + guidToPath[link.guid]);
							if (!noFileID) {
								continue;
							} else {
								link = l;
							}
						}
#else
					foreach(var link in dat.Value.links) {
#endif
						var inboundLinkObject = analyzeData[link];
						if (inboundLinkObject.inboundLinks != null) {
							inboundLinkObject.inboundLinks.Add(dat.Key);
						} else {
							inboundLinkObject.inboundLinks = new List<AssetUniqueID>(){dat.Key};
						}
					}
				}
			}
			foreach(var assetInfo in analyzeData.Values) {
				if (assetInfo.inboundLinks != null) {
					assetInfo.inboundLinks.Sort((x,y)=>{
						var compare = string.Compare(guidToPath[x.guid], guidToPath[y.guid]);
						if (compare == 0) {
							compare = x.fileID - y.fileID;
						}
						return compare;
					});
				}
			}

			++doneCount;
			analyzeProgress = doneCount * progressUnit;
		}
	}
}
