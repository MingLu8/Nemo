﻿using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Nemo;
using Nemo.Configuration;
using Nemo.Configuration.Mapping;
using Nemo.Data;
using Nemo.Linq;
using Nemo.Serialization;
using Nemo.UnitOfWork;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;

namespace NemoTest
{
    class Program
    {
        static void Main(string[] args)
        {
            var config = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json").Build();

            var nemoConfig = ConfigurationFactory.Configure()
                .SetDefaultChangeTrackingMode(ChangeTrackingMode.Debug)
                .SetDefaultMaterializationMode(MaterializationMode.Exact)
                .SetDefaultCacheRepresentation(CacheRepresentation.None)
                .SetDefaultSerializationMode(SerializationMode.Compact)
                .SetOperationNamingConvention(OperationNamingConvention.Default)
                .SetOperationPrefix("spDTO_")
                .SetAutoTypeCoercion(true)
                .SetLogging(false)
                .SetSystemConfiguration(config);

            var settings = ConfigurationFactory.Get(typeof(object)).SystemConfiguration.ConnectionString("DbConnection");

            Console.WriteLine(settings.ConnectionString);

            //var selected_customers_A_count = ObjectFactory.Count<Customer>(c => c.CompanyName.StartsWith("A"));
            //var linqCustomersAsync = new NemoQueryableAsync<Customer>().Where(c => c.Id == "ALFKI").Take(10).Skip(selected_customers_A_count).OrderBy(c => c.Id).FirstOrDefault().Result;

            RunRetrieve(500, true, nemoConfig);
            RunRetrieve(500, false, nemoConfig);
            RunSelect(500, true, nemoConfig);
            RunSelect(500, false, nemoConfig);
            RunNative(500);
            RunEF(500, true);
            RunEF(500, false);
            RunExecute(500);
            RunDapper(500);
            RunNativeWithMapper(500);
        }

        private static void RunEF(int count, bool reuseContext)
        {
            // Warm-up
            using (var context = new EFContext())
            {
                context.ChangeTracker.AutoDetectChangesEnabled = false;
                context.ChangeTracker.QueryTrackingBehavior = Microsoft.EntityFrameworkCore.QueryTrackingBehavior.NoTracking;
                var customer = context.Customers.ToList();
            }

            var timer = new Stopwatch();

            if (reuseContext)
            {
                using (var context = new EFContext())
                {
                    context.ChangeTracker.AutoDetectChangesEnabled = false;
                    context.ChangeTracker.QueryTrackingBehavior = Microsoft.EntityFrameworkCore.QueryTrackingBehavior.NoTracking;

                    for (var i = 0; i < count; i++)
                    {
                        timer.Start();
                        var customer = context.Customers.ToList();//FirstOrDefault(c => c.Id == "ALFKI");
                        timer.Stop();
                    }
                }
            }
            else
            {
                for (var i = 0; i < count; i++)
                {
                    using (var context = new EFContext())
                    {
                        context.ChangeTracker.AutoDetectChangesEnabled = false;
                        context.ChangeTracker.QueryTrackingBehavior = Microsoft.EntityFrameworkCore.QueryTrackingBehavior.NoTracking;

                        timer.Start();
                        var customer = context.Customers.ToList();//FirstOrDefault(c => c.Id == "ALFKI");
                        timer.Stop();
                    }
                }
            }

            Console.WriteLine($"Entity Framework{(reuseContext ? " (re-using DB context)" : "")}: {timer.Elapsed.TotalMilliseconds}");
        }

        private static void RunNativeWithMapper(int count)
        {
            var connection = DbFactory.CreateConnection("DbConnection");
            const string sql = @"select CustomerID, CompanyName from Customers";

            connection.Open();

            // Warm-up
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = sql;
                cmd.CommandType = CommandType.Text;
                //var param = cmd.CreateParameter();
                //param.ParameterName = "CustomerId";
                //param.Value = "ALFKI";
                //cmd.Parameters.Add(param);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var customer = new Customer();
                        ObjectFactory.Map(reader, customer);
                    }
                }
            }

            var timer = new Stopwatch();
            timer.Start();
            for (var i = 0; i < count; i++)
            {
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = sql;
                    cmd.CommandType = CommandType.Text;
                    //var param = cmd.CreateParameter();
                    //param.ParameterName = "CustomerId";
                    //param.Value = "ALFKI";
                    //cmd.Parameters.Add(param);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var customer = new Customer();
                            ObjectFactory.Map(reader, customer);
                        };
                    }
                }
            }
            timer.Stop();
            connection.Close();

            Console.WriteLine("Native+Nemo.Mapper: " + timer.Elapsed.TotalMilliseconds);
        }

        private static void RunNative(int count)
        {
            var connection = DbFactory.CreateConnection("DbConnection");
            const string sql = @"select CustomerID, CompanyName from Customers";

            connection.Open();

            // Warm-up
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = sql;
                cmd.CommandType = CommandType.Text;
                //var param = cmd.CreateParameter();
                //param.ParameterName = "CustomerId";
                //param.Value = "ALFKI";
                //cmd.Parameters.Add(param);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read()) { }
                }
            }

            var timer = new Stopwatch();
            timer.Start();
            for (var i = 0; i < count; i++)
            {
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = sql;
                    cmd.CommandType = CommandType.Text;
                    //var param = cmd.CreateParameter();
                    //param.ParameterName = "CustomerId";
                    //param.Value = "ALFKI";
                    //cmd.Parameters.Add(param);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read()) { };
                    }
                }
            }
            timer.Stop();
            connection.Close();

            Console.WriteLine("Native: " + timer.Elapsed.TotalMilliseconds);
        }

        private static void RunExecute(int count)
        {
            var connection = DbFactory.CreateConnection("DbConnection");

            connection.Open();

            var req = new OperationRequest { Operation = @"select CustomerID, CompanyName from Customers", /*Parameters = new[] { new Param { Name = "CustomerId", Value = "ALFKI", DbType = DbType.String } },*/ ReturnType = OperationReturnType.SingleResult, OperationType = Nemo.OperationType.Sql, Connection = connection };

            // Warm-up
            var response = ObjectFactory.Execute<Customer>(req);
            using (var reader = (IDataReader)response.Value)
            {
                while (reader.Read()) { }
            }

            var timer = new Stopwatch();
            timer.Start();
            for (var i = 0; i < count; i++)
            {
                response = ObjectFactory.Execute<Customer>(req);
                using (var reader = (IDataReader)response.Value)
                {
                    while (reader.Read()) { }
                }
            }
            timer.Stop();
            connection.Close();

            Console.WriteLine("Nemo.Execute:" + timer.Elapsed.TotalMilliseconds);
        }

        private static void RunRetrieve(int count, bool cached, Nemo.Configuration.IConfiguration nemoConfig)
        {
            var connection = DbFactory.CreateConnection("DbConnection", nemoConfig);
            const string sql = @"select CustomerID, CompanyName from Customers";
            //var parameters = new[] { new Param { Name = "CustomerId", Value = "ALFKI", DbType = DbType.String } };

            connection.Open();

            // Warm-up
            var clonedConfig = ConfigurationFactory.CloneConfiguration(nemoConfig);
            clonedConfig.SetLogging(true).SetLogProvider(new ConsoleLoggingProvider());
            var result = ObjectFactory.Retrieve<Customer>(connection: connection, sql: sql, /*parameters: parameters,*/ cached: cached, config: clonedConfig).ToList();

            var timer = new Stopwatch();
            timer.Start();
            for (var i = 0; i < count; i++)
            {
                result = ObjectFactory.Retrieve<Customer>(connection: connection, sql: sql, /*parameters: parameters,*/ cached: cached, config: nemoConfig).ToList();
            }
            timer.Stop();
            connection.Close();

            Console.WriteLine("Nemo.Retrieve: " + timer.Elapsed.TotalMilliseconds);
        }

        private static void RunSelect(int count, bool cached, Nemo.Configuration.IConfiguration nemoConfig)
        {
            var connection = DbFactory.CreateConnection("DbConnection", nemoConfig);
            Expression<Func<Customer, bool>> predicate = c => c.Id == "ALFKI";

            connection.Open();

            // Warm-up
            var clonedConfig = ConfigurationFactory.CloneConfiguration(nemoConfig);
            clonedConfig.SetLogging(true).SetLogProvider(new ConsoleLoggingProvider());
            var result = ObjectFactory.Select<Customer>(null, connection: connection, cached: cached, config: clonedConfig).ToList();

            var timer = new Stopwatch();
            timer.Start();
            for (var i = 0; i < count; i++)
            {
                result = ObjectFactory.Select<Customer>(null, connection: connection, cached: cached, config: nemoConfig).ToList();
            }
            timer.Stop();
            connection.Close();

            Console.WriteLine("Nemo.Select: " + timer.Elapsed.TotalMilliseconds);
        }

        private static void RunDapper(int count)
        {
            var connection = DbFactory.CreateConnection("DbConnection");
            var sql = @"select CustomerID as Id, CompanyName from Customers";

            connection.Open();
            // Warm-up
            var result = connection.Query<Customer>(sql, null /*new { CustomerID = "ALFKI" }*/, buffered: false).ToList();
            var timer = new Stopwatch();
            timer.Start();
            for (var i = 0; i < count; i++)
            {
                result = connection.Query<Customer>(sql, null /*new { CustomerID = "ALFKI" }*/, buffered: false).ToList();
            }
            timer.Stop();

            connection.Close();

            Console.WriteLine("Dapper: " + timer.Elapsed.TotalMilliseconds);
        }
    }
}
