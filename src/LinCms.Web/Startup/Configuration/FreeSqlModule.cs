﻿using Autofac;
using FreeSql;
using FreeSql.Internal;
using LinCms.Entities;
using LinCms.FreeSql;
using LinCms.Utils;
using Microsoft.Extensions.Configuration;
using Serilog;
using System;
using System.Diagnostics;
using System.Threading;
using ToolGood.Words;

namespace LinCms.Startup.Configuration
{
    public class FreeSqlModule : Module
    {
        private readonly IConfiguration _configuration;

        public FreeSqlModule(IConfiguration configuration)
        {
            this._configuration = configuration;
        }

        protected override void Load(ContainerBuilder builder)
        {
            ILogger Logger = Log.Logger;

            IFreeSql fsql = new FreeSqlBuilder()
                .UseConnectionString(_configuration)
                .UseNameConvert(NameConvertType.PascalCaseToUnderscoreWithLower)
                .UseAutoSyncStructure(true)
                .UseNoneCommandParameter(true)
                .UseMonitorCommand(cmd =>
                    {
                        Trace.WriteLine(cmd.CommandText + ";");
                    }
                )
                .CreateDatabaseIfNotExists()
                .Build()
                .SetDbContextOptions(opt => opt.EnableAddOrUpdateNavigateList = true)//联级保存功能开启（默认为关闭）
                ;


            fsql.Aop.CurdAfter += (s, e) =>
            {
                Logger.Debug($"ManagedThreadId:{Thread.CurrentThread.ManagedThreadId}: FullName:{e.EntityType.FullName}" +
                          $" ElapsedMilliseconds:{e.ElapsedMilliseconds}ms, {e.Sql}");


                if (e.ElapsedMilliseconds > 200)
                {
                    //记录日志
                    //发送短信给负责人
                }
            };

            //敏感词处理
            if (_configuration["AuditValue:Enable"].ToBoolean())
            {
                IllegalWordsSearch illegalWords = ToolGoodUtils.GetIllegalWordsSearch();

                fsql.Aop.AuditValue += (s, e) =>
                {
                    if (e.Column.CsType == typeof(string) && e.Value != null)
                    {
                        string oldVal = (string)e.Value;
                        string newVal = illegalWords.Replace(oldVal);
                        //第二种处理敏感词的方式
                        //string newVal = oldVal.ReplaceStopWords();
                        if (newVal != oldVal)
                        {
                            e.Value = newVal;
                        }
                    }
                };
            }

            builder.RegisterInstance(fsql).SingleInstance();
            //services.AddFreeRepository();

            builder.RegisterType(typeof(UnitOfWorkManager)).InstancePerLifetimeScope();

            fsql.GlobalFilter.Apply<IDeleteAduitEntity>("IsDeleted", a => a.IsDeleted == false);
            try
            {
                using var objPool = fsql.Ado.MasterPool.Get();
            }
            catch (Exception e)
            {
                Log.Logger.Error(e + e.StackTrace + e.Message + e.InnerException);
                return;
            }
            //在运行时直接生成表结构
            try
            {
                fsql.CodeFirst
                    .SeedData()
                    .SyncStructure(ReflexHelper.GetTypesByTableAttribute());
            }
            catch (Exception e)
            {
                Log.Logger.Error(e + e.StackTrace + e.Message + e.InnerException);
            }

        }
    }
}
