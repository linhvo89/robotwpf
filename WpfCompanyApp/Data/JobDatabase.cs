using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using WpfCompanyApp.Models;

namespace WpfCompanyApp.Data
{
    public class JobDatabase
    {
        private readonly string _dbPath;

        public JobDatabase(string dbPath = "jobs.db")
        {
            _dbPath = dbPath;
            if (!File.Exists(_dbPath))
                InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();

            string tableCmd = @"CREATE TABLE IF NOT EXISTS Jobs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                JobName TEXT NOT NULL,
                Description TEXT,
                Parameter1 REAL,
                Parameter2 REAL,
                IsActive INTEGER
            );";

            using var cmd = new SqliteCommand(tableCmd, conn);
            cmd.ExecuteNonQuery();
        }


        public List<JobModelHome> GetAllJobsHome()
        {
            var jobs = new List<JobModelHome>();
            try
            {
                using var conn = new SqliteConnection($"Data Source={_dbPath}");
                conn.Open();

                using var cmd = new SqliteCommand("SELECT * FROM Jobs", conn);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    jobs.Add(new JobModelHome
                    {
                        Id = reader.GetInt32(0),
                        JobName = reader.GetString(1),
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Database error: {ex.Message}");
            }

            return jobs;
        }

     
    }
}
