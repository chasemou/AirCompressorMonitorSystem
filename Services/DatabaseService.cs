using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WpfTest.Models;

namespace WpfTest.Services
{
    /// <summary>
    /// SQLite 数据库服务（线程安全）：
    /// 负责报警历史、趋势数据、系统设置的持久化存储。
    /// 数据库文件 monitor.db 放在程序运行目录下。
    /// 内部用 SemaphoreSlim 保证后台线程写库与 UI 线程查库串行化。
    /// </summary>
    public class DatabaseService : IDisposable
    {
        private readonly string _dbPath;
        private readonly SemaphoreSlim _dbLock = new(1, 1);
        private SqliteConnection? _connection;
        private bool _initialized;

        public DatabaseService(string dbFileName = "monitor.db")
        {
            _dbPath = Path.Combine(AppContext.BaseDirectory, dbFileName);
        }

        /// <summary>
        /// 打开连接并创建表（如果不存在）。SQLite 会自动创建 .db 文件，无需手动 File.Create。
        /// </summary>
        public async Task InitializeAsync()
        {
            await _dbLock.WaitAsync();
            try
            {
                if (_initialized) return;

                _connection = new SqliteConnection($"Data Source={_dbPath}");
                await _connection.OpenAsync();

                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Alarms (
                    Id         INTEGER PRIMARY KEY AUTOINCREMENT,
                    DeviceId   TEXT    NOT NULL,
                    ItemName   TEXT    NOT NULL,
                    Message    TEXT    NOT NULL,
                    MesType    TEXT    NOT NULL,
                    MsgTime    TEXT    NOT NULL,
                    RemoveTime TEXT
                );

                CREATE TABLE IF NOT EXISTS TrendData (
                    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    Timestamp   TEXT    NOT NULL,
                    Pressure    REAL,
                    Temperature REAL,
                    Humidity    REAL,
                    Flow        REAL
                );

                CREATE TABLE IF NOT EXISTS Settings (
                    Key   TEXT PRIMARY KEY,
                    Value TEXT NOT NULL
                );";
                await cmd.ExecuteNonQueryAsync();
                _initialized = true;
            }
            finally
            {
                _dbLock.Release();
            }
        }

        #region 报警记录

        /// <summary>插入一条新报警，返回数据库自增 ID</summary>
        public async Task<int> InsertAlarmAsync(AlarmModels alarm)
        {
            await _dbLock.WaitAsync();
            try
            {
                EnsureReady();
                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = @"
                INSERT INTO Alarms (DeviceId, ItemName, Message, MesType, MsgTime, RemoveTime)
                VALUES ($deviceId, $itemName, $message, $mesType, $msgTime, $removeTime);
                SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("$deviceId", alarm.DeviceId);
                cmd.Parameters.AddWithValue("$itemName", alarm.ItemName);
                cmd.Parameters.AddWithValue("$message", alarm.Message);
                cmd.Parameters.AddWithValue("$mesType", alarm.MesType);
                cmd.Parameters.AddWithValue("$msgTime", alarm.MsgTime.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("$removeTime",
                    alarm.RemoveTime.HasValue
                        ? alarm.RemoveTime.Value.ToString("yyyy-MM-dd HH:mm:ss")
                        : (object)DBNull.Value);

                var result = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            }
            finally
            {
                _dbLock.Release();
            }
        }

        /// <summary>报警恢复时回填修复时间</summary>
        public async Task UpdateAlarmRemoveTimeAsync(int dbId, DateTime removeTime)
        {
            await _dbLock.WaitAsync();
            try
            {
                EnsureReady();
                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = "UPDATE Alarms SET RemoveTime = $removeTime WHERE Id = $id";
                cmd.Parameters.AddWithValue("$removeTime", removeTime.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("$id", dbId);
                await cmd.ExecuteNonQueryAsync();
            }
            finally
            {
                _dbLock.Release();
            }
        }

        /// <summary>加载最近 N 条报警记录（按时间倒序）</summary>
        public async Task<List<AlarmModels>> LoadRecentAlarmsAsync(int limit = 50)
        {
            await _dbLock.WaitAsync();
            try
            {
                EnsureReady();
                var list = new List<AlarmModels>();
                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = @"SELECT Id, DeviceId, ItemName, Message, MesType, MsgTime, RemoveTime
                                    FROM Alarms ORDER BY Id DESC LIMIT $limit";
                cmd.Parameters.AddWithValue("$limit", limit);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    list.Add(new AlarmModels
                    {
                        DbId = reader.GetInt32(0),
                        Index = reader.GetInt32(0),
                        DeviceId = reader.GetString(1),
                        ItemName = reader.GetString(2),
                        Message = reader.GetString(3),
                        MesType = reader.GetString(4),
                        MsgTime = DateTime.Parse(reader.GetString(5)),
                        RemoveTime = reader.IsDBNull(6) ? null : DateTime.Parse(reader.GetString(6))
                    });
                }
                return list;
            }
            finally
            {
                _dbLock.Release();
            }
        }

        /// <summary>删除所有已修复（RemoveTime 不为空）的报警记录</summary>
        public async Task DeleteResolvedAlarmsAsync()
        {
            await _dbLock.WaitAsync();
            try
            {
                EnsureReady();
                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = "DELETE FROM Alarms WHERE RemoveTime IS NOT NULL";
                await cmd.ExecuteNonQueryAsync();
            }
            finally
            {
                _dbLock.Release();
            }
        }

        #endregion

        #region 趋势数据

        /// <summary>插入一条趋势采样数据</summary>
        public async Task InsertTrendDataAsync(DateTime timestamp,
            double pressure, double temperature, double humidity, double flow)
        {
            await _dbLock.WaitAsync();
            try
            {
                EnsureReady();
                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = @"
                INSERT INTO TrendData (Timestamp, Pressure, Temperature, Humidity, Flow)
                VALUES ($ts, $p, $t, $h, $f)";
                cmd.Parameters.AddWithValue("$ts", timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("$p", pressure);
                cmd.Parameters.AddWithValue("$t", temperature);
                cmd.Parameters.AddWithValue("$h", humidity);
                cmd.Parameters.AddWithValue("$f", flow);
                await cmd.ExecuteNonQueryAsync();
            }
            finally
            {
                _dbLock.Release();
            }
        }

        /// <summary>查询指定时间范围内的趋势数据</summary>
        public async Task<List<(DateTime Time, double Pressure, double Temperature, double Humidity, double Flow)>>
            LoadTrendDataAsync(DateTime from, DateTime to)
        {
            await _dbLock.WaitAsync();
            try
            {
                EnsureReady();
                var list = new List<(DateTime, double, double, double, double)>();
                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = @"SELECT Timestamp, Pressure, Temperature, Humidity, Flow
                                    FROM TrendData WHERE Timestamp BETWEEN $from AND $to
                                    ORDER BY Id ASC";
                cmd.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd HH:mm:ss"));

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    list.Add((
                        DateTime.Parse(reader.GetString(0)),
                        reader.IsDBNull(1) ? 0 : reader.GetDouble(1),
                        reader.IsDBNull(2) ? 0 : reader.GetDouble(2),
                        reader.IsDBNull(3) ? 0 : reader.GetDouble(3),
                        reader.IsDBNull(4) ? 0 : reader.GetDouble(4)
                    ));
                }
                return list;
            }
            finally
            {
                _dbLock.Release();
            }
        }

        #endregion

        #region 系统设置

        /// <summary>保存单个设置（key 已存在则更新）</summary>
        public async Task SaveSettingAsync(string key, string value)
        {
            await _dbLock.WaitAsync();
            try
            {
                EnsureReady();
                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = @"INSERT INTO Settings (Key, Value) VALUES ($key, $value)
                                    ON CONFLICT(Key) DO UPDATE SET Value = $value";
                cmd.Parameters.AddWithValue("$key", key);
                cmd.Parameters.AddWithValue("$value", value);
                await cmd.ExecuteNonQueryAsync();
            }
            finally
            {
                _dbLock.Release();
            }
        }

        /// <summary>加载全部设置为字典</summary>
        public async Task<Dictionary<string, string>> LoadAllSettingsAsync()
        {
            await _dbLock.WaitAsync();
            try
            {
                EnsureReady();
                var dict = new Dictionary<string, string>();
                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = "SELECT Key, Value FROM Settings";
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    dict[reader.GetString(0)] = reader.GetString(1);
                }
                return dict;
            }
            finally
            {
                _dbLock.Release();
            }
        }

        #endregion

        private void EnsureReady()
        {
            if (!_initialized || _connection == null)
                throw new InvalidOperationException("数据库尚未初始化，请先调用 InitializeAsync()");
        }

        public void Dispose()
        {
            _dbLock.Wait();
            try
            {
                _connection?.Close();
                _connection?.Dispose();
                _connection = null;
                _initialized = false;
            }
            finally
            {
                _dbLock.Release();
            }
            _dbLock.Dispose();
        }
    }
}
