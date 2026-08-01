using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Shapes;
using WpfCompanyApp.Models;
using WpfCompanyApp.Views;

namespace WpfCompanyApp.Data
{
    public class DatabaseRobot
    {
        private readonly string _dbPath = "jobsRobot.db";

        public DatabaseRobot()
        {

            _dbPath = System.IO.Path.Combine(AppContext.BaseDirectory, "jobsRobot.db");

            var csb = new SqliteConnectionStringBuilder
            {
                DataSource = _dbPath,
                Mode = SqliteOpenMode.ReadWriteCreate
            };
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();

            string createJobs = @"CREATE TABLE IF NOT EXISTS Jobs(
                                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                                    Name TEXT,
                                    DatetimeJob TEXT
                                  )";

            string createPoses = @"CREATE TABLE IF NOT EXISTS RobotPoses(
                                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                                    JobId INTEGER,
                                    Name TEXT,
                                    X REAL, Y REAL, Z REAL,
                                    Rx REAL, Ry REAL, Rz REAL,
                                    CreatedAt TEXT,
                                    FOREIGN KEY (JobId) REFERENCES Jobs(Id) ON DELETE CASCADE
                                  )";

            string createRobotSpeedSettings = @"
                CREATE TABLE IF NOT EXISTS RobotSpeedSettings(
                    SpeedName TEXT PRIMARY KEY,
                    SpeedValue REAL NOT NULL,
                    UpdatedAt TEXT NOT NULL DEFAULT (datetime('now'))
                )";

            string createAppSettings = @"
                CREATE TABLE IF NOT EXISTS AppSettings(
                    SettingName TEXT PRIMARY KEY,
                    SettingValue TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL DEFAULT (datetime('now'))
                )";

            string createJobCounters = @"
                CREATE TABLE IF NOT EXISTS JobCounters(
                    JobId INTEGER PRIMARY KEY,
                    Basket1Count REAL NOT NULL DEFAULT 0,
                    Basket2Count REAL NOT NULL DEFAULT 0,
                    TotalCount REAL NOT NULL DEFAULT 0,
                    UpdatedAt TEXT NOT NULL DEFAULT (datetime('now'))
                )";

            using var cmd1 = new SqliteCommand(createJobs, conn);
            using var cmd2 = new SqliteCommand(createPoses, conn);
            using var cmd3 = new SqliteCommand(createRobotSpeedSettings, conn);
            using var cmd4 = new SqliteCommand(createAppSettings, conn);
            using var cmd5 = new SqliteCommand(createJobCounters, conn);
            cmd1.ExecuteNonQuery();
            cmd2.ExecuteNonQuery();
            cmd3.ExecuteNonQuery();
            cmd4.ExecuteNonQuery();
            cmd5.ExecuteNonQuery();

            string[] speedNames =
            {
                "SpeedCapture",
                "SpeedSuction",
                "SpeedMoveToDrop1",
                "SpeedMoveBetweenDrops",
                "SpeedReturnAfterDrop"
            };

            foreach (string speedName in speedNames)
            {
                using var insertDefault = conn.CreateCommand();
                insertDefault.CommandText = @"
                    INSERT OR IGNORE INTO RobotSpeedSettings
                        (SpeedName, SpeedValue)
                    VALUES
                        ($speedName, 0.2);";
                insertDefault.Parameters.AddWithValue("$speedName", speedName);
                insertDefault.ExecuteNonQuery();
            }
        }

        public Dictionary<string, double> GetRobotSpeedSettings()
        {
            var result = new Dictionary<string, double>(StringComparer.Ordinal);

            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT SpeedName, SpeedValue
                FROM RobotSpeedSettings;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result[reader.GetString(0)] = reader.GetDouble(1);

            return result;
        }

        public void SaveRobotSpeedSetting(string speedName, double speedValue)
        {
            if (string.IsNullOrWhiteSpace(speedName))
                throw new ArgumentException("Tên tốc độ không được để trống.", nameof(speedName));

            if (speedValue <= 0 || speedValue > 1)
                throw new ArgumentOutOfRangeException(
                    nameof(speedValue),
                    "Tốc độ phải lớn hơn 0 và không vượt quá 1.");

            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO RobotSpeedSettings
                    (SpeedName, SpeedValue, UpdatedAt)
                VALUES
                    ($speedName, $speedValue, datetime('now'))
                ON CONFLICT(SpeedName) DO UPDATE SET
                    SpeedValue = excluded.SpeedValue,
                    UpdatedAt = datetime('now');";
            cmd.Parameters.AddWithValue("$speedName", speedName);
            cmd.Parameters.AddWithValue("$speedValue", speedValue);
            cmd.ExecuteNonQuery();
        }

        public string GetAppSetting(string settingName, string defaultValue)
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT SettingValue
                FROM AppSettings
                WHERE SettingName = $settingName
                LIMIT 1;";
            cmd.Parameters.AddWithValue("$settingName", settingName);

            object? value = cmd.ExecuteScalar();
            return value is string text && !string.IsNullOrWhiteSpace(text)
                ? text
                : defaultValue;
        }

        public void SaveAppSetting(string settingName, string settingValue)
        {
            if (string.IsNullOrWhiteSpace(settingName))
                throw new ArgumentException("Tên cấu hình không được để trống.", nameof(settingName));

            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO AppSettings
                    (SettingName, SettingValue, UpdatedAt)
                VALUES
                    ($settingName, $settingValue, datetime('now'))
                ON CONFLICT(SettingName) DO UPDATE SET
                    SettingValue = excluded.SettingValue,
                    UpdatedAt = datetime('now');";
            cmd.Parameters.AddWithValue("$settingName", settingName);
            cmd.Parameters.AddWithValue("$settingValue", settingValue ?? string.Empty);
            cmd.ExecuteNonQuery();
        }

        public void GetJobCounters(
            int jobId,
            out double basket1Count,
            out double basket2Count,
            out double totalCount)
        {
            basket1Count = 0;
            basket2Count = 0;
            totalCount = 0;

            if (jobId <= 0)
                return;

            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Basket1Count, Basket2Count, TotalCount
                FROM JobCounters
                WHERE JobId = $jobId
                LIMIT 1;";
            cmd.Parameters.AddWithValue("$jobId", jobId);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return;

            basket1Count = reader.GetDouble(0);
            basket2Count = reader.GetDouble(1);
            totalCount = reader.GetDouble(2);
        }

        public void SaveJobCounters(
            int jobId,
            double basket1Count,
            double basket2Count,
            double totalCount)
        {
            if (jobId <= 0)
                return;

            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO JobCounters
                    (JobId, Basket1Count, Basket2Count, TotalCount, UpdatedAt)
                VALUES
                    ($jobId, $basket1Count, $basket2Count, $totalCount, datetime('now'))
                ON CONFLICT(JobId) DO UPDATE SET
                    Basket1Count = excluded.Basket1Count,
                    Basket2Count = excluded.Basket2Count,
                    TotalCount = excluded.TotalCount,
                    UpdatedAt = datetime('now');";
            cmd.Parameters.AddWithValue("$jobId", jobId);
            cmd.Parameters.AddWithValue("$basket1Count", Math.Max(0, basket1Count));
            cmd.Parameters.AddWithValue("$basket2Count", Math.Max(0, basket2Count));
            cmd.Parameters.AddWithValue("$totalCount", Math.Max(0, totalCount));
            cmd.ExecuteNonQuery();
        }

        public void SaveCalibPointsToDb(RobotPointCalib[] points, string namecalib)
        {
            if (points == null || points.Length == 0) return;

            string dbPath = System.IO.Path.Combine(AppContext.BaseDirectory, "jobsRobot.db");
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            conn.Open();

            // đảm bảo bảng tồn tại
            using (var create = conn.CreateCommand())
            {
                create.CommandText = @"
            CREATE TABLE IF NOT EXISTS calib_points (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                namecalib  TEXT NOT NULL,
                imagex     REAL NOT NULL DEFAULT 0,
                imagey     REAL NOT NULL DEFAULT 0,
                robotx     REAL NOT NULL DEFAULT 0,
                roboty     REAL NOT NULL DEFAULT 0,
                angle      REAL NOT NULL DEFAULT 0,
                created_at TEXT DEFAULT (datetime('now'))
            );";
                create.ExecuteNonQuery();
            }

            // ✅ XÓA HẾT DÒNG CŨ THEO namecalib (tool1, tool2...)
            using (var del = conn.CreateCommand())
            {
                del.CommandText = "DELETE FROM calib_points WHERE namecalib = $name;";
                del.Parameters.AddWithValue("$name", namecalib);
                del.ExecuteNonQuery();
            }

            using var tx = conn.BeginTransaction();

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
            INSERT INTO calib_points (namecalib, imagex, imagey, robotx, roboty, angle)
            VALUES ($name, $imagex, $imagey, $robotx, $roboty, $angle);";

            var pName = cmd.CreateParameter(); pName.ParameterName = "$name"; cmd.Parameters.Add(pName);
            var pImageX = cmd.CreateParameter(); pImageX.ParameterName = "$imagex"; cmd.Parameters.Add(pImageX);
            var pImageY = cmd.CreateParameter(); pImageY.ParameterName = "$imagey"; cmd.Parameters.Add(pImageY);
            var pRobotX = cmd.CreateParameter(); pRobotX.ParameterName = "$robotx"; cmd.Parameters.Add(pRobotX);
            var pRobotY = cmd.CreateParameter(); pRobotY.ParameterName = "$roboty"; cmd.Parameters.Add(pRobotY);
            var pAngle = cmd.CreateParameter(); pAngle.ParameterName = "$angle"; cmd.Parameters.Add(pAngle);

            foreach (var pt in points)
            {
                pName.Value = namecalib;
                pImageX.Value = pt.ImageX;
                pImageY.Value = pt.ImageY;
                pRobotX.Value = pt.RobotX;
                pRobotY.Value = pt.RobotY;
                pAngle.Value = pt.Angle;

                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }
        public List<RobotPointCalib> GetCalibPoints(string namecalib)
        {
            var result = new List<RobotPointCalib>();

            string dbPath = System.IO.Path.Combine(AppContext.BaseDirectory, "jobsRobot.db");
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
        SELECT namecalib, imagex, imagey, robotx, roboty, angle
        FROM calib_points
        WHERE namecalib = $name;";
            cmd.Parameters.AddWithValue("$name", namecalib);

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                result.Add(new RobotPointCalib
                {
                    NameCalib = rd.GetString(0),
                    ImageX = Math.Round(rd.GetDouble(1), 3),
                    ImageY = Math.Round(rd.GetDouble(2), 3),
                    RobotX = Math.Round(rd.GetDouble(3), 3),
                    RobotY = Math.Round(rd.GetDouble(4), 3),
                    Angle = Math.Round(rd.GetDouble(5), 3),
                });
            }

            return result;
        }
        public bool IsJobModelExists(string modelName)
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT 1
                FROM JobsName
                WHERE JobsName = $modelName
                LIMIT 1;
            ";
            cmd.Parameters.AddWithValue("$modelName", modelName);
            using var reader = cmd.ExecuteReader();
            return reader.Read();
        }
        public void InsertJobModel(string modelName)
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO JobsName
                (
                    JobsName,
                    H1, H2, H3,
                    V1, V2, V3, V4, V5, V6,
                    a1, a2, a3, a4, a5, a6,
                    R,
                    SelectedJob,
                    CreatedAt
                )
                VALUES
                (
                    $JobsName,
                    0, 0, 0,
                    0, 0, 0, 0, 0, 0,
                    0, 0, 0, 0, 0, 0,
                    0,
                    0,
                    datetime('now')
                );
            ";

            cmd.Parameters.AddWithValue("$JobsName", modelName);
            cmd.ExecuteNonQuery();
        }
        public List<JobModelSetting> GetJobs()
        {
            var result = new List<JobModelSetting>();

            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, JobsName, 
                CreatedAt
                FROM JobsName;
            ";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new JobModelSetting
                {
                    Id = reader.GetInt32(0),
                    JobName = reader.GetString(1),
                    DatetimeJob = DateTime.Parse(reader.GetString(2))
                });
            }

            return result;
        }
        public List<JobModelHome> GetJobsName()
        {
            var list = new List<JobModelHome>();

            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
        SELECT 
            Id,
            JobsName,
            H1, H2, H3,
            R,
            CreatedAt
        FROM JobsName
        ORDER BY CreatedAt DESC;
    ";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new JobModelHome
                {
                    Id = reader.GetInt32(0),
                    JobName = reader.GetString(1),

                    H1 = reader.GetDouble(2),
                    H2 = reader.GetDouble(3),
                    H3 = reader.GetDouble(4),

                    R = reader.GetDouble(5),
                    DatetimeJob = DateTime.Parse(reader.GetString(6))
                });
            }

            return list;
        }

        public void UpdateJobHomeValue(int jobId, string columnName, double value)
        {
            string column = columnName switch
            {
                "H1" => "H1",
                "H2" => "H2",
                "H3" => "H3",
                "R" => "R",
                _ => throw new ArgumentException("Cột không hợp lệ.", nameof(columnName))
            };

            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();

            var cmd = conn.CreateCommand();
            cmd.CommandText = $"UPDATE JobsName SET {column} = $value WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$value", value);
            cmd.Parameters.AddWithValue("$id", jobId);
            cmd.ExecuteNonQuery();
        }


        public void DeleteJobModelByName(string jobName)
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                DELETE FROM JobsName
                WHERE JobsName = $jobName;
            ";

            cmd.Parameters.AddWithValue("$jobName", jobName);
            cmd.ExecuteNonQuery();
        }
        // ------------------- POSE -------------------

        public ObservableCollection<RobotPose> GetRobotPoses(int jobId)
        {
            var list = new ObservableCollection<RobotPose>();
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();

            string sql = "SELECT * FROM RobotPoses WHERE JobId=@id";
            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", jobId);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                var createdAtStr = reader["CreatedAt"]?.ToString();
                DateTime createdAt = DateTime.MinValue;

                if (!string.IsNullOrEmpty(createdAtStr))
                    DateTime.TryParse(createdAtStr, out createdAt);

                list.Add(new RobotPose
                {
                    Id = Convert.ToInt32(reader["Id"]),
                    JobId = Convert.ToInt32(reader["JobId"]),
                    Name = reader["Name"]?.ToString(),
                    X = Convert.ToDouble(reader["X"]),
                    Y = Convert.ToDouble(reader["Y"]),
                    Z = Convert.ToDouble(reader["Z"]),
                    Rx = Convert.ToDouble(reader["Rx"]),
                    Ry = Convert.ToDouble(reader["Ry"]),
                    Rz = Convert.ToDouble(reader["Rz"]),
                    CreatedAt = createdAt,
                    IsEnabled = Convert.ToInt32(reader["IsEnabled"]) == 1 // ✅ đọc trạng thái
                });
            }

            return list;
        }

        public void AddPose(RobotPose pose)
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();

            string sql = @"INSERT INTO RobotPoses (JobId, Name, X, Y, Z, Rx, Ry, Rz, CreatedAt)
                           VALUES (@j,@n,@x,@y,@z,@rx,@ry,@rz,@c)";
            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@j", pose.JobId);
            cmd.Parameters.AddWithValue("@n", pose.Name ?? "");
            cmd.Parameters.AddWithValue("@x", pose.X);
            cmd.Parameters.AddWithValue("@y", pose.Y);
            cmd.Parameters.AddWithValue("@z", pose.Z);
            cmd.Parameters.AddWithValue("@rx", pose.Rx);
            cmd.Parameters.AddWithValue("@ry", pose.Ry);
            cmd.Parameters.AddWithValue("@rz", pose.Rz);
            cmd.Parameters.AddWithValue("@c", pose.CreatedAt.ToString("s"));
            cmd.ExecuteNonQuery();
        }


        public void DeletePose(int poseId)
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            using var cmd = new SqliteCommand("DELETE FROM RobotPoses WHERE Id=@id", conn);
            cmd.Parameters.AddWithValue("@id", poseId);
            cmd.ExecuteNonQuery();
        }

        public void UpdateTrajectory(RobotTrajectory data)
        {
            string sql = @"
            UPDATE RobotTRAJECTORY
            SET
                X = @X,
                Y = @Y,
                Z = @Z,
                Rx = @Rx,
                Ry = @Ry,
                Rz = @Rz,
                J1 = @J1,
                J2 = @J2,
                J3 = @J3,
                J4 = @J4,
                J5 = @J5,
                J6 = @J6,
                CreatedAt = @CreatedAt
            WHERE NamePoses = @NamePoses;
            ";
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            using (var cmd = new SqliteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@X", data.X);
                cmd.Parameters.AddWithValue("@Y", data.Y);
                cmd.Parameters.AddWithValue("@Z", data.Z);
                cmd.Parameters.AddWithValue("@Rx", data.Rx);
                cmd.Parameters.AddWithValue("@Ry", data.Ry);
                cmd.Parameters.AddWithValue("@Rz", data.Rz);
                cmd.Parameters.AddWithValue("@J1", data.J1);
                cmd.Parameters.AddWithValue("@J2", data.J2);
                cmd.Parameters.AddWithValue("@J3", data.J3);
                cmd.Parameters.AddWithValue("@J4", data.J4);
                cmd.Parameters.AddWithValue("@J5", data.J5);
                cmd.Parameters.AddWithValue("@J6", data.J6);
                //    cmd.Parameters.AddWithValue("@IsEnabled", data.IsEnabled);

                cmd.Parameters.AddWithValue("@CreatedAt", DateTime.Now.ToString());
                cmd.Parameters.AddWithValue("@NamePoses", data.NamePoses);
                int rows = cmd.ExecuteNonQuery();
                if (rows == 0)
                {
                    InsertTrajectory(data);
                }
            }
        }

        private void InsertTrajectory(RobotTrajectory data)
        {
            string sql = @"
            INSERT INTO RobotTRAJECTORY
            (
                JobId,
                Name,
                MoveType,
                NamePoses,
                X,
                Y,
                Z,
                Rx,
                Ry,
                Rz,
                J1,
                J2,
                J3,
                J4,
                J5,
                J6,
                v,
                a,
                IsEnabled,
                CreatedAt
            )
            VALUES
            (
                @JobId,
                @Name,
                @MoveType,
                @NamePoses,
                @X,
                @Y,
                @Z,
                @Rx,
                @Ry,
                @Rz,
                @J1,
                @J2,
                @J3,
                @J4,
                @J5,
                @J6,
                @v,
                @a,
                @IsEnabled,
                @CreatedAt
            );
            ";

            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@JobId", data.JobId);
            cmd.Parameters.AddWithValue("@Name", data.Name ?? data.NamePoses);
            cmd.Parameters.AddWithValue("@MoveType", data.MoveType.ToString());
            cmd.Parameters.AddWithValue("@NamePoses", data.NamePoses);
            cmd.Parameters.AddWithValue("@X", data.X);
            cmd.Parameters.AddWithValue("@Y", data.Y);
            cmd.Parameters.AddWithValue("@Z", data.Z);
            cmd.Parameters.AddWithValue("@Rx", data.Rx);
            cmd.Parameters.AddWithValue("@Ry", data.Ry);
            cmd.Parameters.AddWithValue("@Rz", data.Rz);
            cmd.Parameters.AddWithValue("@J1", data.J1);
            cmd.Parameters.AddWithValue("@J2", data.J2);
            cmd.Parameters.AddWithValue("@J3", data.J3);
            cmd.Parameters.AddWithValue("@J4", data.J4);
            cmd.Parameters.AddWithValue("@J5", data.J5);
            cmd.Parameters.AddWithValue("@J6", data.J6);
            cmd.Parameters.AddWithValue("@v", data.v);
            cmd.Parameters.AddWithValue("@a", data.a);
            cmd.Parameters.AddWithValue("@IsEnabled", data.IsEnabled);
            cmd.Parameters.AddWithValue("@CreatedAt", DateTime.Now.ToString());
            cmd.ExecuteNonQuery();
        }
        public void UpdateVel(RobotTrajectory data)
        {
            string sql = @"
            UPDATE RobotTRAJECTORY
            SET
                v = @v,
                CreatedAt = @CreatedAt
            WHERE NamePoses = @NamePoses;
            ";
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            using (var cmd = new SqliteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@v", data.v);
                cmd.Parameters.AddWithValue("@CreatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("@NamePoses", data.NamePoses);

                cmd.ExecuteNonQuery();
            }
        }
        public List<RobotTrajectory> GetRobotTrajectories()
        {
            var list = new List<RobotTrajectory>();

            using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
            {
                conn.Open();

                string sql = @"SELECT * FROM RobotTRAJECTORY";

                using (var cmd = new SqliteCommand(sql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var trajectory = new RobotTrajectory
                        {
                            Id = Convert.ToInt32(reader["Id"]),
                            JobId = Convert.ToInt32(reader["JobId"]),
                            Name = reader["Name"]?.ToString(),

                            // ⭐ MoveType TEXT → enum
                            MoveType = ParseMoveType(reader["MoveType"]?.ToString()),

                            NamePoses = reader["NamePoses"]?.ToString(),

                            X = Convert.ToDouble(reader["X"]),
                            Y = Convert.ToDouble(reader["Y"]),
                            Z = Convert.ToDouble(reader["Z"]),
                            Rx = Convert.ToDouble(reader["Rx"]),
                            Ry = Convert.ToDouble(reader["Ry"]),
                            Rz = Convert.ToDouble(reader["Rz"]),

                            J1 = Convert.ToDouble(reader["J1"]),
                            J2 = Convert.ToDouble(reader["J2"]),
                            J3 = Convert.ToDouble(reader["J3"]),
                            J4 = Convert.ToDouble(reader["J4"]),
                            J5 = Convert.ToDouble(reader["J5"]),
                            J6 = Convert.ToDouble(reader["J6"]),

                            v = Convert.ToDouble(reader["v"]),
                            a = Convert.ToDouble(reader["a"]),

                            IsEnabled = Convert.ToInt32(reader["IsEnabled"])
                        };

                        list.Add(trajectory);
                    }
                }
            }

            return list;
        }
        public RobotTrajectory GetRobotTrajectoryByNamePoses(string namePoses)
        {
            using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
            {
                conn.Open();

                string sql = @"SELECT * 
                       FROM RobotTRAJECTORY 
                       WHERE NamePoses = @NamePoses
                       LIMIT 1";   // chỉ lấy 1 dòng

                using (var cmd = new SqliteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@NamePoses", namePoses);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new RobotTrajectory
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                JobId = Convert.ToInt32(reader["JobId"]),
                                Name = reader["Name"]?.ToString(),
                                MoveType = ParseMoveType(reader["MoveType"]?.ToString()),
                                NamePoses = reader["NamePoses"]?.ToString(),

                                X = Convert.ToDouble(reader["X"]),
                                Y = Convert.ToDouble(reader["Y"]),
                                Z = Convert.ToDouble(reader["Z"]),
                                Rx = Convert.ToDouble(reader["Rx"]),
                                Ry = Convert.ToDouble(reader["Ry"]),
                                Rz = Convert.ToDouble(reader["Rz"]),

                                J1 = Convert.ToDouble(reader["J1"]),
                                J2 = Convert.ToDouble(reader["J2"]),
                                J3 = Convert.ToDouble(reader["J3"]),
                                J4 = Convert.ToDouble(reader["J4"]),
                                J5 = Convert.ToDouble(reader["J5"]),
                                J6 = Convert.ToDouble(reader["J6"]),

                                v = Convert.ToDouble(reader["v"]),
                                a = Convert.ToDouble(reader["a"]),

                                IsEnabled = Convert.ToInt32(reader["IsEnabled"])
                            };
                        }
                    }
                }
            }

            return null; // không tìm thấy
        }

        public void UpdateMoveTypeByNamePoses(string namePoses, RobotTrajectory.MoveTypeEnum moveType)
        {
            using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
            {
                conn.Open();

                string sql = @"UPDATE RobotTRAJECTORY 
                       SET MoveType = @MoveType 
                       WHERE NamePoses = @NamePoses";

                using (var cmd = new SqliteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@MoveType", moveType.ToString()); // "moveL" hoặc "moveJ"
                    cmd.Parameters.AddWithValue("@NamePoses", namePoses);

                    cmd.ExecuteNonQuery();
                }
            }
        }
        // ⭐ HÀM NÀY BẮT BUỘC PHẢI NẰM TRONG CLASS DatabaseRobot
        private RobotTrajectory.MoveTypeEnum ParseMoveType(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return RobotTrajectory.MoveTypeEnum.moveL;

            if (Enum.TryParse(value, out RobotTrajectory.MoveTypeEnum type))
                return type;

            return RobotTrajectory.MoveTypeEnum.moveL;
        }
        // ====== 15 Ô PHÔI: LƯU / LOAD TOÀN CỤC KHÔNG THEO JOB ======

        /// <summary>
        /// Đọc SlotsMask từ TableSp. Nếu chưa có dòng nào thì tạo 1 dòng = 0.
        /// </summary>
        public int GetSlotsMask()
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();

            const string sqlSelect = @"SELECT SlotsMask FROM TableSp LIMIT 1;";

            using var cmd = new SqliteCommand(sqlSelect, conn);
            object? result = cmd.ExecuteScalar();

            if (result == null || result == DBNull.Value)
            {
                // Chưa có dòng -> tạo 1 dòng mặc định
                const string sqlInsert = @"INSERT INTO TableSp (SlotsMask) VALUES (0);";
                using var cmdInsert = new SqliteCommand(sqlInsert, conn);
                cmdInsert.ExecuteNonQuery();
                return 0;
            }

            return Convert.ToInt32(result);
        }

        /// <summary>
        /// Ghi SlotsMask vào TableSp. Nếu chưa có dòng thì INSERT, nếu có rồi thì UPDATE.
        /// </summary>
        public void SaveSlotsMask(int mask)
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();

            const string sqlUpdate = @"UPDATE TableSp SET SlotsMask = $mask;";

            using (var cmdUpdate = new SqliteCommand(sqlUpdate, conn))
            {
                cmdUpdate.Parameters.AddWithValue("$mask", mask);
                int rows = cmdUpdate.ExecuteNonQuery();

                if (rows == 0)
                {
                    // Chưa có dòng nào => INSERT
                    const string sqlInsert = @"INSERT INTO TableSp (SlotsMask) VALUES ($mask);";
                    using var cmdInsert = new SqliteCommand(sqlInsert, conn);
                    cmdInsert.Parameters.AddWithValue("$mask", mask);
                    cmdInsert.ExecuteNonQuery();
                }
            }
        }

    }
}
