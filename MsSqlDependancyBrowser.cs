﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Xsl;
using SqlScriptParser;

namespace MsSqlDependancyBrowser
{
    class Program
    {
        static class MainClass
        {
            static HashSet<string> keywords;
            static XslCompiledTransform xslTranCompiler;
            const string objectNameParam = "sp";
            const string clientUrl = "http://localhost:8085/";

            static void Main()
            {
                keywords = new HashSet<string>(Resources.keywords.Split(' '));

                xslTranCompiler = new XslCompiledTransform();
                var xslDoc = new XmlDocument();
                xslDoc.LoadXml(Resources.table2html);
                xslTranCompiler.Load(xslDoc);

                var web = new HttpListener();

                web.Prefixes.Add(clientUrl);

                Console.WriteLine("Listening..");

                web.Start();

                for (;;)
                {
                    var context = web.GetContext();

                    var response = context.Response;
                    Console.WriteLine(context.Request.RawUrl);
                    Console.WriteLine(context.Request.HttpMethod);

                    string result = "";
                    string spName = "";
                    foreach (string key in context.Request.QueryString.Keys)
                    {
                        if (key == objectNameParam)
                        {
                            spName = context.Request.QueryString[key];
                            result = requestDatabase(spName);
                        }

                        Console.WriteLine($"key {key} value {context.Request.QueryString[key]}");
                    }

                    string responseString = $@" <html>
                                                    <head><title>{spName}</title></head>
                                                    <body><pre>{result}</pre></body>
                                                </html>";

                    var buffer = Encoding.UTF8.GetBytes(responseString);

                    response.ContentLength64 = buffer.Length;

                    var output = response.OutputStream;

                    output.Write(buffer, 0, buffer.Length);

                    Console.WriteLine(output);

                    output.Close(); 
                }

                web.Stop();

                Console.ReadKey();
            }

            static string requestDatabase(string spName)
            {
                string connectionString = @"Data Source=PC;Initial Catalog=test;Integrated Security=True";
                string queryQbjectInfo = Resources.queryObjectInfo;
                string queryObjectDependancies = Resources.queryObjectDependancies;
                string queryTableXml = Resources.queryTableXml;

                try
                {
                    using (var sqlConn = new SqlConnection(connectionString))
                    {
                        var sqlCmd = new SqlCommand(queryQbjectInfo, sqlConn);
                        sqlConn.Open();
                        sqlCmd.Parameters.Add("@objectName", SqlDbType.NVarChar);
                        sqlCmd.Parameters["@objectName"].Value = spName;

                        string objectFullName = "";
                        string object_text = "";
                        string type_desc = "";
                        using (SqlDataReader dr = sqlCmd.ExecuteReader())
                        {
                            if (dr.HasRows && dr.Read())
                            {
                                object_text = dr.IsDBNull(0) ? "" : dr.GetString(0);
                                objectFullName = dr.GetString(1);
                                type_desc = dr.GetString(2);
                            }
                        }

                        if (type_desc == "USER_TABLE")
                        {
                            sqlCmd = new SqlCommand(queryTableXml, sqlConn);
                            sqlCmd.Parameters.Add("@objectName", SqlDbType.NVarChar);
                            sqlCmd.Parameters["@objectName"].Value = spName;

                            string tableInfoXml = "";
                            using (SqlDataReader dr = sqlCmd.ExecuteReader())
                            {
                                if (dr.Read())
                                {
                                    tableInfoXml = dr.GetString(0);
                                }
                            }

                            var xmlSource = new XmlDocument();
                            var htmlDest = new StringBuilder();
                            xmlSource.LoadXml(tableInfoXml);
                            xslTranCompiler.Transform(xmlSource, XmlWriter.Create(htmlDest));
                            return htmlDest.ToString();
                        }

                        if (objectFullName != "")
                        {
                            sqlCmd = new SqlCommand(queryObjectDependancies, sqlConn);
                            sqlCmd.Parameters.Add("@objectFullName", SqlDbType.NVarChar);
                            sqlCmd.Parameters["@objectFullName"].Value = objectFullName;
                            
                            var depList = new Dictionary<string, string>();
                            using (SqlDataReader dr = sqlCmd.ExecuteReader())
                            {
                                while (dr.Read())
                                {
                                    string depName = dr.GetString(0);
                                    string typeDesc = dr.IsDBNull(1) ? "UNKNOWN_OBJECT" : dr.GetString(1);
                                    depList[depName.ToLower()] = $"<a href='{clientUrl}?{objectNameParam}={depName}' title='{typeDesc}'>{depName}<a>";
                                }
                            }

                            var wordProcessor = new WordProcessor(keywords, depList);
                            var singleCommentProcessor = new BlockProcessor(wordProcessor, @"--.*[\r\n]", "green");
                            var commentAndStringProcessor = new CommentAndStringProcessor(singleCommentProcessor);
                            return commentAndStringProcessor.Process(object_text);
                        }
                        return "object not exists";
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Exception: {ex}");
                    return $"Exception: {ex}";
                }
            }
        }
    }
}