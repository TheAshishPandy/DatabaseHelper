using System.Data;
using System.Data.Common;

namespace DatabaseHelper
{
    public interface IDatabaseHelper
    {
        int ExecuteNonQuery(string connectionString, CommandType commandType, string commandText, params DbParameter[] parameters);
        DataSet ExecuteDataset(string connectionString, CommandType commandType, string commandText, params DbParameter[] parameters);
        DbDataReader ExecuteReader(string connectionString, CommandType commandType, string commandText, params DbParameter[] parameters);
        object ExecuteScalar(string connectionString, CommandType commandType, string commandText, params DbParameter[] parameters);
        
        DbParameter CreateParameter(string name, DbType dbType);
        DbParameter CreateParameter(string name, object value);
    }
}