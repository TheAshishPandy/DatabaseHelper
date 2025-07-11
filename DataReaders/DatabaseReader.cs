using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;

namespace DatabaseHelper.DataReaders
{
    /// <summary>
    /// Provides a strongly-typed wrapper around IDataReader with null-safe value retrieval
    /// </summary>
    public class DatabaseReader : IDisposable
    {
        private readonly IDataReader _reader;
        private int[] _columnOrdinals;
        private bool _isOptimized; 
        private bool _isFirstRow = true;
        private int _columnIndex = -2;
        private static readonly ConcurrentDictionary<string, Dictionary<string, int>> _ordinalLookup = new();
        private Dictionary<string, int> _ordinals;

        public IDataReader Reader => _reader;
        public object this[string columnName] => _reader[columnName];

        public DatabaseReader(IDataReader reader, bool optimize = false)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _isOptimized = optimize;
            InitializeOptimization();
        }

        private void InitializeOptimization()
        {
            if (_isOptimized && _columnOrdinals == null)
            {
                _columnOrdinals = new int[_reader.FieldCount];
            }
        }

        public bool Read()
        {
            if (_isFirstRow)
            {
                _isFirstRow = false;
            }

            if (_isOptimized && _columnIndex == -2)
            {
                _isFirstRow = true;
            }

            _columnIndex = -1;
            return _reader.Read();
        }

        private int GetOrdinal(string name)
        {
            if (!_isOptimized)
            {
                return _ordinals?[name] ?? _reader.GetOrdinal(name);
            }

            _columnIndex++;
            return _isOptimized && !_isFirstRow 
                ? _columnOrdinals[_columnIndex] 
                : (_columnOrdinals[_columnIndex] = _reader.GetOrdinal(name));
        }

        #region Value Getters
        public long GetInt64(string columnName, long defaultValue = -1)
        {
            int index = GetOrdinal(columnName);
            object value = _reader.GetValue(index);
            return value == DBNull.Value ? defaultValue : Convert.ToInt64(value);
        }

        public int GetInt32(string columnName, int defaultValue = -1)
        {
            int index = GetOrdinal(columnName);
            object value = _reader.GetValue(index);
            return value == DBNull.Value ? defaultValue : Convert.ToInt32(value);
        }

        public short GetInt16(string columnName, short defaultValue = -1)
        {
            int index = GetOrdinal(columnName);
            object value = _reader.GetValue(index);
            return value == DBNull.Value ? defaultValue : Convert.ToInt16(value);
        }

        public string GetString(string columnName, string defaultValue = "")
        {
            int index = GetOrdinal(columnName);
            object value = _reader.GetValue(index);
            return value == DBNull.Value ? defaultValue : value.ToString();
        }

        public bool GetBoolean(string columnName, bool defaultValue = false)
        {
            int index = GetOrdinal(columnName);
            if (_reader.IsDBNull(index)) return defaultValue;

            Type fieldType = _reader.GetFieldType(index);
            return fieldType switch
            {
                Type t when t == typeof(bool) => _reader.GetBoolean(index),
                Type t when t == typeof(int) => _reader.GetInt32(index) != 0,
                Type t when t == typeof(long) => _reader.GetInt64(index) != 0,
                Type t when t == typeof(short) => _reader.GetInt16(index) != 0,
                _ => Convert.ToBoolean(_reader.GetValue(index))
            };
        }

        public decimal GetDecimal(string columnName, decimal defaultValue = 0m)
        {
            int index = GetOrdinal(columnName);
            object value = _reader.GetValue(index);
            return value == DBNull.Value ? defaultValue : Convert.ToDecimal(value);
        }

        public DateTime GetDateTime(string columnName, DateTime defaultValue = default)
        {
            int index = GetOrdinal(columnName);
            object value = _reader.GetValue(index);
            return value == DBNull.Value ? defaultValue : Convert.ToDateTime(value);
        }

        public byte[] GetBytes(string columnName)
        {
            int index = GetOrdinal(columnName);
            object value = _reader.GetValue(index);
            return value == DBNull.Value ? null : (byte[])value;
        }

        public double GetDouble(string columnName, double defaultValue = 0.0)
        {
            int index = GetOrdinal(columnName);
            object value = _reader.GetValue(index);
            return value == DBNull.Value ? defaultValue : Convert.ToDouble(value);
        }

        public Guid GetGuid(string columnName)
        {
            int index = GetOrdinal(columnName);
            object value = _reader.GetValue(index);
            return value == DBNull.Value ? default : new Guid(value.ToString());
        }
        #endregion

        #region Helper Methods
        public bool IsDBNull(string columnName)
        {
            return _reader.IsDBNull(GetOrdinal(columnName));
        }

        public void SkipColumns(int count)
        {
            _columnIndex += count;
        }

        public void OptimizeOrdinals(string cacheKey)
        {
            _ordinals = _ordinalLookup.GetOrAdd(cacheKey, key =>
            {
                var dict = new Dictionary<string, int>(_reader.FieldCount, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < _reader.FieldCount; i++)
                {
                    dict[_reader.GetName(i)] = i;
                }
                return dict;
            });
        }
        #endregion

        public void Dispose()
        {
            _reader?.Dispose();
        }
    }
}