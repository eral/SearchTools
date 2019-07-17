//Tiny Serializable Dictionary
//2019.7.17 Yuichi.Higuchi

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SearchTools
{
	[System.Serializable]
	public class SerializableDictionary<TKey, TValue> : IDictionary<TKey, TValue>
	{
		[SerializeField] private List<TKey> _keys = new List<TKey>();
		[SerializeField] private List<TValue> _values = new List<TValue>();

		private Dictionary<TKey, TValue> _dictionary = null;
		private Dictionary<TKey, TValue> dic
		{
			get
			{
				if (_dictionary == null)
				{
					Deserialize();
				}

				return _dictionary;
			}
			set
			{
				_dictionary = value;
				Serialize();
			}
		}
		
		public void Add(TKey key, TValue value)
		{
			if (Contains(new KeyValuePair<TKey, TValue>(key, value))) return;
			dic.Add(key, value);
			_keys.Add(key);
			_values.Add(value);
		}

		public void Add(KeyValuePair<TKey, TValue> pair)
		{
			Add(pair.Key, pair.Value);
		}

		public bool Contains(KeyValuePair<TKey, TValue> pair)
		{
			if (dic.ContainsKey(pair.Key))
			{
				if (dic[pair.Key].Equals(pair.Value))
				{
					return true;
				} 
			}
			return  false;
		}

		public bool ContainsKey(TKey key)
		{
			return dic.ContainsKey(key);
		}

		public void CopyTo(KeyValuePair<TKey, TValue>[] pairs, int count)
		{
			int i = 0;
			foreach (var kvp in dic)
			{
				pairs[i] = kvp;
				i++;
			}
		}

		bool ICollection<KeyValuePair<TKey,TValue>>.IsReadOnly
		{
			get {
				return false;
			}
		}

		bool ICollection<KeyValuePair<TKey,TValue>>.Remove(KeyValuePair<TKey,TValue> pair)
		{
			return dic.Remove(pair.Key);
		}

		bool IDictionary<TKey, TValue>.Remove(TKey key)
		{
			return dic.Remove(key);
		}

		bool IDictionary<TKey, TValue>.TryGetValue(TKey key, out TValue value)
		{
			return dic.TryGetValue(key, out value);
		}
		
		TValue IDictionary<TKey, TValue>.this[TKey key]
		{
			get { return dic[key]; }
			set
			{
				dic[key] = value;
				Serialize();
			}
		}

		public TValue this[TKey key]
		{
			get { return dic[key]; }
			set
			{
				dic[key] = value;
				Serialize();
			}
		}

		public int Count
		{
			get
			{
				return dic.Count;
			}
		}

		public ICollection<TKey> Keys
		{
			get { return dic.Keys; }
		}

		public ICollection<TValue> Values
		{
			get { return dic.Values; }
		}

		IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
		{
			return dic.GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return dic.GetEnumerator();
		}
		
		public void Clear()
		{
			dic.Clear();
			_keys.Clear();
			_values.Clear();
		}

		void Serialize()
		{
			_keys.Clear();
			_values.Clear();
			foreach (var kvp in _dictionary)
			{
				_keys.Add(kvp.Key);
				_values.Add(kvp.Value);
			}
		}

		void Deserialize()
		{
			_dictionary = new Dictionary<TKey, TValue>();
			for (int i = 0; i < Mathf.Min(_keys.Count, _values.Count); i++)
			{
				_dictionary.Add(_keys[i], _values[i]);
			}
		}

	}
}
