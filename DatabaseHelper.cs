using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;
using Npgsql;


namespace DatabaseHelper
{
    public sealed class DatabaseHelper
    {
        private enum DbConnectionOwnership
        {
            Internal,
            External
        }

        public enum DatabaseType
        {
            SqlServer,
            Oracle,
            PostgreSQL
        }

        private static DbProviderFactory _factory;
        private static string _parameterPrefix = "@";
        private static int _defaultQueryTimeout = 30;
        private static DatabaseType _currentDatabaseType;

        [ThreadStatic]
        private static int _queryTimeout;

        public static DbProviderFactory Factory
        {
            get => _factory;
            set => throw new InvalidOperationException("Use SetDatabaseType() instead");
        }

        public static string ParameterPrefix => _parameterPrefix;

        public static int DefaultQueryTimeout
        {
            get => _defaultQueryTimeout;
            set => _defaultQueryTimeout = value > 0 ? value : _defaultQueryTimeout;
        }

        public static int QueryTimeout
        {
            get => _queryTimeout == 0 ? _defaultQueryTimeout : _queryTimeout;
            set => _queryTimeout = value;
        }

        public static DatabaseType CurrentDatabaseType => _currentDatabaseType;

        static DatabaseHelper()
        {
            // Default to SQL Server
            SetDatabaseType(DatabaseType.SqlServer);
        }

        public static void SetDatabaseType(DatabaseType dbType)
        {
            _currentDatabaseType = dbType;

            _factory = dbType switch
            {
                DatabaseType.Oracle => OracleClientFactory.Instance,
                DatabaseType.PostgreSQL => NpgsqlFactory.Instance,
                _ => SqlClientFactory.Instance
            };

            _parameterPrefix = dbType switch
            {
                DatabaseType.Oracle => ":",
                _ => "@"
            };
        }

        public static void ResetTimeout()
        {
            _queryTimeout = 0;
        }

        private DatabaseHelper() { }

        #region Core Methods

        private static void AttachParameters(DbCommand command, DbParameter[] commandParameters)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (commandParameters == null) return;

            foreach (var parameter in commandParameters)
            {
                if (parameter != null)
                {
                    if ((parameter.Direction == ParameterDirection.InputOutput ||
                         parameter.Direction == ParameterDirection.Input) &&
                        parameter.Value == null)
                    {
                        parameter.Value = DBNull.Value;
                    }

                    // Handle PostgreSQL specific parameter types
                    if (_currentDatabaseType == DatabaseType.PostgreSQL && parameter is NpgsqlParameter npgsqlParam)
                    {
                        // Special handling for JSON types
                        if (parameter.Value is string && (npgsqlParam.NpgsqlDbType == NpgsqlTypes.NpgsqlDbType.Json ||
                                                         npgsqlParam.NpgsqlDbType == NpgsqlTypes.NpgsqlDbType.Jsonb))
                        {
                            parameter.Value = parameter.Value;
                        }
                    }

                    command.Parameters.Add(parameter);
                }
            }
        }

        private static bool PrepareCommand(DbCommand command, DbConnection connection,
            DbTransaction transaction, CommandType commandType, string commandText,
            DbParameter[] commandParameters, out bool mustCloseConnection)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (string.IsNullOrEmpty(commandText))
                throw new ArgumentNullException(nameof(commandText));

            command.CommandText = commandText;
            command.CommandType = commandType;

            if (connection.State != ConnectionState.Open)
            {
                mustCloseConnection = true;
                connection.Open();
            }
            else
            {
                mustCloseConnection = false;
            }

            command.Connection = connection;
            command.CommandTimeout = QueryTimeout;

            if (transaction != null)
            {
                if (transaction.Connection == null)
                    throw new ArgumentException("The transaction was rollbacked or committed, please provide an open transaction.", nameof(transaction));
                command.Transaction = transaction;
            }

            if (commandParameters != null)
            {
                AttachParameters(command, commandParameters);
            }

            // Oracle specific initialization
            if (_currentDatabaseType == DatabaseType.Oracle && command is OracleCommand oracleCmd)
            {
                oracleCmd.BindByName = true; // Important for Oracle parameter binding
            }

            return false;
        }

        #endregion

        #region Parameter Creation Methods

        public static DbCommand CreateCommand()
        {
            return _factory?.CreateCommand();
        }

        public static DbParameter CreateParameter(string parameterName, DbType dbType)
        {
            var param = _factory.CreateParameter();
            param.ParameterName = GetParameterName(parameterName);
            param.DbType = dbType;
            return param;
        }

        public static DbParameter CreateParameter(string parameterName, DbType dbType, int size)
        {
            var param = CreateParameter(parameterName, dbType);
            param.Size = size;
            return param;
        }

        public static DbParameter CreateParameter(string parameterName, object value)
        {
            var param = _factory.CreateParameter();
            param.ParameterName = GetParameterName(parameterName);
            param.Value = value ?? DBNull.Value;
            return param;
        }

        // Oracle specific parameter creation
        public static DbParameter CreateOracleParameter(string parameterName, OracleDbType oracleType, object value = null)
        {
            if (_currentDatabaseType != DatabaseType.Oracle)
                throw new InvalidOperationException("This method is only valid for Oracle databases");

            var param = (OracleParameter)_factory.CreateParameter();
            param.ParameterName = GetParameterName(parameterName);
            param.OracleDbType = oracleType;
            param.Value = value ?? DBNull.Value;
            return param;
        }

        // PostgreSQL specific parameter creation
        public static DbParameter CreatePostgresParameter(string parameterName, NpgsqlTypes.NpgsqlDbType pgType, object value = null)
        {
            if (_currentDatabaseType != DatabaseType.PostgreSQL)
                throw new InvalidOperationException("This method is only valid for PostgreSQL databases");

            var param = (NpgsqlParameter)_factory.CreateParameter();
            param.ParameterName = GetParameterName(parameterName);
            param.NpgsqlDbType = pgType;
            param.Value = value ?? DBNull.Value;
            return param;
        }
		
		public static DbParameter CreateParameter(string parameterName, DbType dbType, object value, ParameterDirection direction)
		{
			DbParameter dbParameter = _factory.CreateParameter();
			dbParameter.ParameterName = GetParameterName(parameterName);
			dbParameter.DbType = dbType;
			dbParameter.Value = value ?? DBNull.Value;
			dbParameter.Direction = direction;
			return dbParameter;
		}

        private static string GetParameterName(string name)
        {
            if (name.StartsWith(ParameterPrefix))
                return name;

            return ParameterPrefix + name;
        }

        #endregion

        #region Execute Methods (same as before, but with enhanced database-specific handling)

        public static int ExecuteNonQuery(string connectionString, CommandType commandType,
            string commandText, params DbParameter[] commandParameters)
        {
            if (string.IsNullOrEmpty(connectionString))
                throw new ArgumentNullException(nameof(connectionString));

            using var connection = _factory.CreateConnection();
            connection.ConnectionString = connectionString;
            return ExecuteNonQuery(connection, commandType, commandText, commandParameters);
        }

        public static int ExecuteNonQuery(DbConnection connection, CommandType commandType,
            string commandText, params DbParameter[] commandParameters)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            var cmd = CreateCommand();
            bool mustCloseConnection = false;

            try
            {
                PrepareCommand(cmd, connection, null, commandType, commandText,
                    commandParameters, out mustCloseConnection);

                int result = cmd.ExecuteNonQuery();
                cmd.Parameters.Clear();
                return result;
            }
            finally
            {
                if (mustCloseConnection)
                    connection.Close();
            }
        }

        public static DataSet ExecuteDataset(string connectionString, CommandType commandType,
            string commandText, params DbParameter[] commandParameters)
        {
            if (string.IsNullOrEmpty(connectionString))
                throw new ArgumentNullException(nameof(connectionString));

            using var connection = _factory.CreateConnection();
            connection.ConnectionString = connectionString;
            return ExecuteDataset(connection, commandType, commandText, commandParameters);
        }

        public static DataSet ExecuteDataset(DbConnection connection, CommandType commandType,
            string commandText, params DbParameter[] commandParameters)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            var cmd = CreateCommand();
            bool mustCloseConnection = false;

            try
            {
                PrepareCommand(cmd, connection, null, commandType, commandText,
                    commandParameters, out mustCloseConnection);

                using var adapter = CreateDataAdapter(cmd);
                var dataset = new DataSet();
                adapter.Fill(dataset);
                cmd.Parameters.Clear();
                return dataset;
            }
            finally
            {
                if (mustCloseConnection)
                    connection.Close();
            }
        }

        public static DbDataReader ExecuteReader(string connectionString, CommandType commandType,
            string commandText, params DbParameter[] commandParameters)
        {
            if (string.IsNullOrEmpty(connectionString))
                throw new ArgumentNullException(nameof(connectionString));

            var connection = _factory.CreateConnection();
            connection.ConnectionString = connectionString;

            try
            {
                connection.Open();
                return ExecuteReader(connection, null, commandType, commandText,
                    commandParameters, DbConnectionOwnership.Internal);
            }
            catch
            {
                connection.Close();
                throw;
            }
        }

        private static DbDataReader ExecuteReader(DbConnection connection, DbTransaction transaction,
            CommandType commandType, string commandText, DbParameter[] commandParameters,
            DbConnectionOwnership connectionOwnership)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            var cmd = CreateCommand();
            bool mustCloseConnection = false;

            try
            {
                PrepareCommand(cmd, connection, transaction, commandType,
                    commandText, commandParameters, out mustCloseConnection);

                return connectionOwnership == DbConnectionOwnership.External
                    ? cmd.ExecuteReader()
                    : cmd.ExecuteReader(CommandBehavior.CloseConnection);
            }
            catch
            {
                if (mustCloseConnection)
                    connection.Close();
                throw;
            }
        }

        public static object ExecuteScalar(string connectionString, CommandType commandType,
            string commandText, params DbParameter[] commandParameters)
        {
            if (string.IsNullOrEmpty(connectionString))
                throw new ArgumentNullException(nameof(connectionString));

            using var connection = _factory.CreateConnection();
            connection.ConnectionString = connectionString;
            return ExecuteScalar(connection, commandType, commandText, commandParameters);
        }

        public static object ExecuteScalar(DbConnection connection, CommandType commandType,
            string commandText, params DbParameter[] commandParameters)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            var cmd = CreateCommand();
            bool mustCloseConnection = false;

            try
            {
                PrepareCommand(cmd, connection, null, commandType, commandText,
                    commandParameters, out mustCloseConnection);

                object result = cmd.ExecuteScalar();
                cmd.Parameters.Clear();
                return result;
            }
            finally
            {
                if (mustCloseConnection)
                    connection.Close();
            }
        }

        #endregion

        #region Async Execute Methods

        public static async Task<int> ExecuteNonQueryAsync(string connectionString, CommandType commandType,
            string commandText, params DbParameter[] commandParameters)
        {
            if (string.IsNullOrEmpty(connectionString))
                throw new ArgumentNullException(nameof(connectionString));

            using var connection = _factory.CreateConnection();
            connection.ConnectionString = connectionString;
            return await ExecuteNonQueryAsync(connection, commandType, commandText, commandParameters);
        }

        public static async Task<int> ExecuteNonQueryAsync(DbConnection connection, CommandType commandType,
            string commandText, params DbParameter[] commandParameters)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            var cmd = CreateCommand();
            bool mustCloseConnection = false;

            try
            {
                PrepareCommand(cmd, connection, null, commandType, commandText,
                    commandParameters, out mustCloseConnection);

                int result = await cmd.ExecuteNonQueryAsync();
                cmd.Parameters.Clear();
                return result;
            }
            finally
            {
                if (mustCloseConnection)
                    await connection.CloseAsync();
            }
        }

        public static async Task<DataSet> ExecuteDatasetAsync(string connectionString, CommandType commandType,
            string commandText, params DbParameter[] commandParameters)
        {
            if (string.IsNullOrEmpty(connectionString))
                throw new ArgumentNullException(nameof(connectionString));

            using var connection = _factory.CreateConnection();
            connection.ConnectionString = connectionString;
            return await ExecuteDatasetAsync(connection, commandType, commandText, commandParameters);
        }

        public static async Task<DataSet> ExecuteDatasetAsync(DbConnection connection, CommandType commandType,
            string commandText, params DbParameter[] commandParameters)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            var cmd = CreateCommand();
            bool mustCloseConnection = false;

            try
            {
                PrepareCommand(cmd, connection, null, commandType, commandText,
                    commandParameters, out mustCloseConnection);

                using var adapter = CreateDataAdapter(cmd);
                var dataset = new DataSet();
                await Task.Run(() => adapter.Fill(dataset));
                cmd.Parameters.Clear();
                return dataset;
            }
            finally
            {
                if (mustCloseConnection)
                    await connection.CloseAsync();
            }
        }

        public static async Task<DbDataReader> ExecuteReaderAsync(string connectionString, CommandType commandType,
            string commandText, params DbParameter[] commandParameters)
        {
            if (string.IsNullOrEmpty(connectionString))
                throw new ArgumentNullException(nameof(connectionString));

            var connection = _factory.CreateConnection();
            connection.ConnectionString = connectionString;

            try
            {
                await connection.OpenAsync();
                return await ExecuteReaderAsync(connection, null, commandType, commandText,
                    commandParameters, DbConnectionOwnership.Internal);
            }
            catch
            {
                await connection.CloseAsync();
                throw;
            }
        }

        private static async Task<DbDataReader> ExecuteReaderAsync(DbConnection connection, DbTransaction transaction,
            CommandType commandType, string commandText, DbParameter[] commandParameters,
            DbConnectionOwnership connectionOwnership)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            var cmd = CreateCommand();
            bool mustCloseConnection = false;

            try
            {
                PrepareCommand(cmd, connection, transaction, commandType,
                    commandText, commandParameters, out mustCloseConnection);

                return connectionOwnership == DbConnectionOwnership.External
                    ? await cmd.ExecuteReaderAsync()
                    : await cmd.ExecuteReaderAsync(CommandBehavior.CloseConnection);
            }
            catch
            {
                if (mustCloseConnection)
                    await connection.CloseAsync();
                throw;
            }
        }

        public static async Task<object> ExecuteScalarAsync(string connectionString, CommandType commandType,
            string commandText, params DbParameter[] commandParameters)
        {
            if (string.IsNullOrEmpty(connectionString))
                throw new ArgumentNullException(nameof(connectionString));

            using var connection = _factory.CreateConnection();
            connection.ConnectionString = connectionString;
            return await ExecuteScalarAsync(connection, commandType, commandText, commandParameters);
        }

        public static async Task<object> ExecuteScalarAsync(DbConnection connection, CommandType commandType,
            string commandText, params DbParameter[] commandParameters)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            var cmd = CreateCommand();
            bool mustCloseConnection = false;

            try
            {
                PrepareCommand(cmd, connection, null, commandType, commandText,
                    commandParameters, out mustCloseConnection);

                object result = await cmd.ExecuteScalarAsync();
                cmd.Parameters.Clear();
                return result;
            }
            finally
            {
                if (mustCloseConnection)
                    await connection.CloseAsync();
            }
        }

        #endregion

        #region Transaction Support

        public static async Task<DbTransaction> BeginTransactionAsync(string connectionString, IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
        {
            if (string.IsNullOrEmpty(connectionString))
                throw new ArgumentNullException(nameof(connectionString));

            var connection = _factory.CreateConnection();
            connection.ConnectionString = connectionString;
            await connection.OpenAsync();
            return await connection.BeginTransactionAsync(isolationLevel);
        }

        public static async Task CommitTransactionAsync(DbTransaction transaction)
        {
            if (transaction == null)
                throw new ArgumentNullException(nameof(transaction));

            await transaction.CommitAsync();
            await transaction.Connection.CloseAsync();
        }

        public static async Task RollbackTransactionAsync(DbTransaction transaction)
        {
            if (transaction == null)
                throw new ArgumentNullException(nameof(transaction));

            await transaction.RollbackAsync();
            await transaction.Connection.CloseAsync();
        }

        #endregion

        #region Bulk Operations (SQL Server specific)

        public static async Task BulkInsertAsync(string connectionString, string tableName, DataTable dataTable)
        {
            if (_currentDatabaseType != DatabaseType.SqlServer)
                throw new NotSupportedException("BulkInsert is only supported for SQL Server");

            using var connection = new SqlConnection(connectionString);
            using var bulkCopy = new SqlBulkCopy(connection);

            bulkCopy.DestinationTableName = tableName;
            bulkCopy.BatchSize = 1000;
            bulkCopy.BulkCopyTimeout = QueryTimeout;

            await connection.OpenAsync();
            await bulkCopy.WriteToServerAsync(dataTable);
        }

        #endregion

        #region Database-Specific Helpers

        private static DbDataAdapter CreateDataAdapter(DbCommand command)
        {
            var adapter = _factory.CreateDataAdapter();
            adapter.SelectCommand = command;
            return adapter;
        }


        // PostgreSQL specific array parameter helper
        public static DbParameter CreatePostgresArrayParameter<T>(string parameterName, T[] array)
        {
            if (_currentDatabaseType != DatabaseType.PostgreSQL)
                throw new InvalidOperationException("This method is only valid for PostgreSQL databases");

            var param = (NpgsqlParameter)_factory.CreateParameter();
            param.ParameterName = GetParameterName(parameterName);

            Type elementType = typeof(T);
            if (elementType == typeof(int))
                param.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Integer;
            else if (elementType == typeof(string))
                param.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text;
            // Add more type mappings as needed

            param.Value = array;
            return param;
        }
        public static OracleRefCursor GetRefCursor(DbParameter parameter)
        {
            if (_currentDatabaseType != DatabaseType.Oracle)
                throw new InvalidOperationException("This method is only valid for Oracle databases");

            if (parameter == null || !(parameter is OracleParameter oracleParam))
                throw new ArgumentException("Parameter must be an OracleParameter");

            if (oracleParam.OracleDbType != OracleDbType.RefCursor)
                throw new ArgumentException("Parameter must be of type RefCursor");

            return (OracleRefCursor)(oracleParam.Value ?? throw new InvalidOperationException("RefCursor value is null"));
        }

        #endregion
    }
}