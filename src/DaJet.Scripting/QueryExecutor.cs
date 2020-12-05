﻿using DaJet.Metadata;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace DaJet.Scripting
{
    public sealed class QueryRequest
    {
        public string QueryName { get; set; }
        public string QueryText { get; set; }
        public IDictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();
    }
    public sealed class QueryResponse
    {
        public object Result { get; set; } // scalar value, table, command result, json, error code
        public IList<string> Errors { get; set; } = new List<string>();
    }

    public interface IQueryExecutor
    {
        /// <summary>
        /// Executes SQL script and returns result as JSON.
        /// </summary>
        string ExecuteJson(string sql);
        string ExecuteJsonString(string sql);
        string ExecuteValue(string sql);
        string ExecuteTable(string sql);
        string ExecuteCommand(string sql);
        void ExecuteScript(TSqlScript script);
    }
    public sealed class QueryExecutor: IQueryExecutor
    {
        private IMetadataService MetadataService { get; }
        public QueryExecutor(IMetadataService metadata)
        {
            MetadataService = metadata ?? throw new ArgumentNullException(nameof(metadata));
        }
        public string ExecuteJson(string sql)
        {
            string json;
            JsonWriterOptions options = new JsonWriterOptions { Indented = true };
            using (MemoryStream stream = new MemoryStream())
            {
                using (Utf8JsonWriter writer = new Utf8JsonWriter(stream, options))
                {
                    writer.WriteStartArray();
                    using (SqlConnection connection = new SqlConnection(MetadataService.ConnectionString))
                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        connection.Open();

                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            var schema = reader.GetColumnSchema();
                            while (reader.Read())
                            {
                                writer.WriteStartObject();
                                for (int c = 0; c < schema.Count; c++)
                                {
                                    object value = reader[c];
                                    string typeName = schema[c].DataTypeName;
                                    string columnName = schema[c].ColumnName;
                                    int valueSize = 0;
                                    if (schema[c].ColumnSize.HasValue)
                                    {
                                        valueSize = schema[c].ColumnSize.Value;
                                    }
                                    if (value == DBNull.Value)
                                    {
                                        writer.WriteNull(columnName);
                                    }
                                    else if (DbUtilities.IsString(typeName))
                                    {
                                        writer.WriteString(columnName, (string)value);
                                    }
                                    else if (DbUtilities.IsDateTime(typeName))
                                    {
                                        writer.WriteString(columnName, ((DateTime)value).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture));
                                    }
                                    else if (DbUtilities.IsVersion(typeName))
                                    {
                                        writer.WriteString(columnName, $"0x{DbUtilities.ByteArrayToString((byte[])value)}");
                                    }
                                    else if (DbUtilities.IsBoolean(typeName, valueSize))
                                    {
                                        if (typeName == "bit")
                                        {
                                            writer.WriteBoolean(columnName, (bool)value);
                                        }
                                        else // binary(1)
                                        {
                                            writer.WriteBoolean(columnName, DbUtilities.GetInt32((byte[])value) == 0 ? false : true);
                                        }
                                    }
                                    else if (DbUtilities.IsNumber(typeName, valueSize))
                                    {
                                        if (typeName == "binary" || typeName == "varbinary") // binary(4) | varbinary(4)
                                        {
                                            writer.WriteNumber(columnName, DbUtilities.GetInt32((byte[])value));
                                        }
                                        else
                                        {
                                            writer.WriteNumber(columnName, (decimal)value);
                                        }
                                    }
                                    else if (DbUtilities.IsUUID(typeName, valueSize))
                                    {
                                        writer.WriteString(columnName, (new Guid((byte[])value)).ToString());
                                    }
                                    else if (DbUtilities.IsReference(typeName, valueSize))
                                    {
                                        byte[] reference = (byte[])value;
                                        int code = DbUtilities.GetInt32(reference[0..4]);
                                        Guid uuid = new Guid(reference[4..^0]);
                                        writer.WriteString(columnName, $"{{{code}:{uuid}}}");
                                    }
                                    else if (DbUtilities.IsBinary(typeName))
                                    {
                                        writer.WriteBase64String(columnName, (byte[])value);
                                    }
                                }
                                writer.WriteEndObject();
                            }
                        }
                    }
                    writer.WriteEndArray();
                }
                json = Encoding.UTF8.GetString(stream.ToArray());
            }
            return json;
        }

        public string ExecuteJsonString(string sql)
        {
            string json;
            using (StringWriter writer = new StringWriter())
            {
                writer.Write("[");
                using (SqlConnection connection = new SqlConnection(MetadataService.ConnectionString))
                using (SqlCommand command = new SqlCommand(sql, connection))
                {
                    connection.Open();
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        var schema = reader.GetColumnSchema();
                        while (reader.Read())
                        {
                            writer.Write("{");
                            for (int c = 0; c < schema.Count; c++)
                            {
                                object value = reader[c];
                                string typeName = schema[c].DataTypeName;
                                string columnName = schema[c].ColumnName;
                                int valueSize = 0;
                                if (schema[c].ColumnSize.HasValue)
                                {
                                    valueSize = schema[c].ColumnSize.Value;
                                }
                                if (value == DBNull.Value)
                                {
                                    writer.Write($"\"{columnName}\":null");
                                }
                                else if (DbUtilities.IsString(typeName))
                                {
                                    writer.Write($"\"{columnName}\":\"{(string)value}\"");
                                }
                                else if (DbUtilities.IsDateTime(typeName))
                                {
                                    writer.Write($"\"{columnName}\":\"{((DateTime)value).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)}\"");
                                }
                                else if (DbUtilities.IsVersion(typeName))
                                {
                                    writer.Write($"\"{columnName}\":\"0x{DbUtilities.ByteArrayToString((byte[])value)}\"");
                                }
                                else if (DbUtilities.IsBoolean(typeName, valueSize))
                                {
                                    if (typeName == "bit")
                                    {
                                        writer.Write($"\"{columnName}\":{((bool)value ? "true" : "false")}");
                                    }
                                    else // binary(1)
                                    {
                                        writer.Write($"\"{columnName}\":{(DbUtilities.GetInt32((byte[])value) == 0 ? "false" : "true")}");
                                    }
                                }
                                else if (DbUtilities.IsNumber(typeName, valueSize))
                                {
                                    if (typeName == "binary" || typeName == "varbinary") // binary(4) | varbinary(4)
                                    {
                                        writer.Write($"\"{columnName}\":{DbUtilities.GetInt32((byte[])value)}");
                                    }
                                    else
                                    {
                                        writer.Write($"\"{columnName}\":{((decimal)value).ToString().Replace(',', '.')}");
                                    }
                                }
                                else if (DbUtilities.IsUUID(typeName, valueSize))
                                {
                                    writer.Write($"\"{columnName}\":\"{new Guid((byte[])value).ToString().ToLower()}\"");
                                }
                                else if (DbUtilities.IsReference(typeName, valueSize))
                                {
                                    byte[] reference = (byte[])value;
                                    int code = DbUtilities.GetInt32(reference[0..4]);
                                    Guid uuid = new Guid(reference[4..^0]);
                                    writer.Write($"\"{columnName}\":\"{{{code}:{uuid}}}\"");
                                }
                                else if (DbUtilities.IsBinary(typeName))
                                {
                                    writer.Write($"\"{columnName}\":\"{Convert.ToBase64String((byte[])value)}\"");
                                }
                                if (c < schema.Count - 1)
                                {
                                    writer.Write(",");
                                }
                                else
                                {
                                    writer.Write("");
                                }
                            }
                            writer.Write("}");
                            writer.Write(",");
                        }
                    }
                }
                json = writer.ToString().TrimEnd(',') + "]";
            }
            return json;
        }

        public string ExecuteValue(string sql)
        {
            throw new NotImplementedException();
        }
        public string ExecuteTable(string sql)
        {
            throw new NotImplementedException();
        }
        public string ExecuteCommand(string sql)
        {
            throw new NotImplementedException();
        }
        public void ExecuteScript(TSqlScript script)
        {
            {
                SqlConnection connection = new SqlConnection(MetadataService.ConnectionString);
                SqlCommand command = connection.CreateCommand();
                command.CommandType = CommandType.Text;
                try
                {
                    connection.Open();

                    foreach (TSqlBatch batch in script.Batches)
                    {
                        command.CommandText = batch.ToSqlString();
                        _ = command.ExecuteNonQuery();
                    }
                }
                catch (Exception error)
                {
                    // TODO: log error
                    _ = error.Message;
                    throw;
                }
                finally
                {
                    if (command != null) command.Dispose();
                    if (connection != null) connection.Dispose();
                }
            }
        }
    }
}