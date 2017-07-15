﻿using CitizenFX.Core;
using CitizenFX.Core.Native;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Data;
using MySql.Data.MySqlClient;

namespace vRP
{
  public class MySQL : BaseScript
  {
    public struct Connection{
      public Connection(IDbConnection con)
      {
        connection = (MySqlConnection)con;
        commands = new Dictionary<string, MySqlCommand>();
      }

      public MySqlConnection connection;
      public Dictionary<string, MySqlCommand> commands; 
    }

    private Dictionary<uint, Task<object[]>> tasks = new Dictionary<uint, Task<object[]>>();
    private Dictionary<string, Connection> connections = new Dictionary<string, Connection>();
    private uint task_id;

    public MySQL()
    {
      task_id = 0;
      Exports.Add("createConnection", new Action<string,string>((name, config) => {
        Console.WriteLine("[vRP/C#] create connection "+name);
      }));
      Exports.Add("createCommand", new Action<string,string>(e_createCommand));
      Exports.Add("query", new Func<string,Dictionary<string,object>,int>(e_query));
      Exports.Add("checkTask", new Func<int,object>(e_checkTask));
    }

    //return [con,cmd] from "con/cmd"
    private string[] parsePath(string path)
    {
      var args = path.Split('/');
      if(args.Length >= 2)
        return args;
      else
        return new string[]{"none","none"};
    }

    // createConnection("conid", "host=...")
    public void e_createConnection(string name, string config)
    {
      var connection = new Connection(new MySqlConnection(config));
      Console.WriteLine("[vRP/C#] create connection "+name);
      connections.Add(name, connection);
    }

    // createCommand("conid/name", "SELECT...")
    public void e_createCommand(string path, string sql)
    {
      var concmd = parsePath(path);

      Connection connection;
      if(connections.TryGetValue(concmd[0], out connection)){
        using (MySqlCommand cmd = (MySqlCommand)connection.connection.CreateCommand())
        {
          cmd.CommandText = sql;
          connection.commands.Add(concmd[1], cmd);
          Console.WriteLine("[vRP/C#] create command "+path);
        }
      }
    }

    // query("con/cmd", {...})
    public int e_query(string path, Dictionary<string,object> parameters)
    {
      var concmd = parsePath(path);
      var task = -1;

      Connection connection;
      if(connections.TryGetValue(concmd[0], out connection)){
        MySqlCommand command;
        if(connection.commands.TryGetValue(concmd[1], out command)){
          tasks.Add(task_id, Task.Run(async () => {
            await connection.connection.OpenAsync();

            //set parameters
            foreach(var param in parameters ?? Enumerable.Empty<KeyValuePair<string, object>>())
              command.Parameters.AddWithValue("@"+param.Key, param.Value);

            using (var reader = await command.ExecuteReaderAsync())
            {
              var results = new List<Dictionary<string, object>>();

              while (await reader.ReadAsync())
              {
                var entry = new Dictionary<string, object>();
                for (int i = 0; i < reader.FieldCount; i++)
                  entry[reader.GetName(i)] = reader.GetValue(i);

                results.Add(entry);
              }

              return new object[]{
                results, reader.RecordsAffected
              };
            }
          }));

          task = (int)task_id++;
        }

        Console.WriteLine("[vRP/C#] query "+path+" id "+task);
      }

      return task;
    }

    public object e_checkTask(int id)
    {
      Task<object[]> task = null;
      if(tasks.TryGetValue((uint)id, out task)){
        if(!task.IsFaulted && task.IsCompleted){
          var r = (object[])task.Result;
          Console.WriteLine("[vRP/C#] send back mysql result to "+id);
          tasks.Remove((uint)id);
          return new object[]{true, r[0], r[1]};
        }
      }

      return new object[]{false};
    }
  }
}