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

            using var cmd1 = new SqliteCommand(createJobs, conn);
            using var cmd2 = new SqliteCommand(createPoses, conn);
            cmd1.ExecuteNonQuery();
            cmd2.ExecuteNonQuery();
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
                    // Không có bản ghi nào được update – có thể NamePoses không tồn tại
                }
            }
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
