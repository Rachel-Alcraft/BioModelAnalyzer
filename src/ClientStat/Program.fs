// Copyright (c) Microsoft Research 2016
// License: MIT. See LICENSE

open System
open System.IO
open DataAccess
open FSharp.Charting
open System.Windows.Forms
open System.Drawing
open Microsoft.Azure
open Microsoft.WindowsAzure.Storage.Table
open Microsoft.WindowsAzure.Storage

type Row = { PartitionKey : string;
             RowKey : string;
             LogInTime : DateTime; 
             LogOutTime : DateTime;
             FurtherTestingCount : int;
             RunProofCount : int;
             RunSimulationCount : int;
             ProofErrorCount : int;
             SimulationErrorCount : int;
             FurtherTestingErrorCount : int;
             ImportModelCount : int;
             NewModelCount : int;
             SaveModelCount : int }

let tryParse s = 
    let mutable r = 0
    if Int32.TryParse(s, &r) then r else 0

let stylize (c : ChartTypes.GenericChart) = 
    let vs = ChartTypes.LabelStyle.Create(Angle = -90)
    c.WithXAxis(Title = "Weeks", LabelStyle = vs)

[<EntryPoint>]
[<STAThread>]
let main argv = 
    if(argv.Length <> 1) then
        printfn "Usage: ClientStat storage-account-connection-string"
        0
    else
        printfn "Reading data from Azure..."

        let account = CloudStorageAccount.Parse(argv.[0])
        let tableClient = account.CreateCloudTableClient()
        let caTable = tableClient.GetTableReference("ClientActivity")

        if caTable.Exists() then
            let rows = caTable.ExecuteQuery(new TableQuery())
            let rows = rows |> Seq.map(fun r -> { LogInTime = r.["LogInTime"].DateTime.Value
                                                  LogOutTime = r.["LogOutTime"].DateTime.Value
                                                  PartitionKey = r.PartitionKey
                                                  RowKey = r.RowKey
                                                  FurtherTestingCount = r.["FurtherTestingCount"].Int32Value.Value
                                                  RunProofCount = r.["RunProofCount"].Int32Value.Value
                                                  RunSimulationCount = r.["RunSimulationCount"].Int32Value.Value
                                                  ProofErrorCount = r.["ProofErrorCount"].Int32Value.Value
                                                  SimulationErrorCount = r.["SimulationErrorCount"].Int32Value.Value
                                                  FurtherTestingErrorCount = r.["FurtherTestingErrorCount"].Int32Value.Value
                                                  ImportModelCount = r.["ImportModelCount"].Int32Value.Value
                                                  NewModelCount = r.["NewModelCount"].Int32Value.Value
                                                  SaveModelCount = r.["SaveModelCount"].Int32Value.Value }) 
                                     |> List.ofSeq

            let r = rows |> Seq.head
            let start = rows |> Seq.map (fun r -> r.LogInTime) |> Seq.min
            let en = rows |> Seq.map (fun r -> r.LogInTime) |> Seq.max
            let totalCount = rows |> Seq.length
            let unique = rows |> Seq.countBy(fun r -> r.PartitionKey) |> Seq.length
            printfn "Start date: %A" start
            printfn "End date: %A" en
            printfn "Total sessions: %d" totalCount
            printfn "Unique users: %d" unique

            printfn "Writing all data to bma.csv..."
            use writer = new StreamWriter("bma.csv")
            writer.WriteLine("ClientID,SessionID,LogInTime,LogOutTime,FurtherTestingCount,RunProofCount,RunSimulationCount,ProofErrorCount,SimulationErrorCount,FurtherTestingErrorCount,ImportModelCount,NewModelCount,SaveModelCount")
            let writeRow r = sprintf "%s,%s,\"%s\",\"%s\",%d,%d,%d,%d,%d,%d,%d,%d,%d"
                                     r.PartitionKey
                                     r.RowKey
                                     (r.LogInTime.ToString())
                                     (r.LogOutTime.ToString())
                                     r.FurtherTestingCount
                                     r.RunProofCount
                                     r.RunSimulationCount
                                     r.ProofErrorCount
                                     r.SimulationErrorCount
                                     r.FurtherTestingErrorCount
                                     r.ImportModelCount
                                     r.NewModelCount
                                     r.SaveModelCount
                              |> writer.WriteLine
            rows |> Seq.iter writeRow
            writer.Flush() 

            printfn "Writing weekly averages to file..."
            let mutable start = start.Subtract(TimeSpan.FromDays(float(start.DayOfWeek)))
            start <- DateTime(start.Year, start.Month, start.Day) // Align to the beginning of week
            use writer = new StreamWriter("bma_weekly.csv")
            writer.WriteLine("date, SessionCount, UniqueUsers, Experiments, Authoring, Errors, TotalTime")
            let mutable s_sessions = []
            let mutable s_unique = []
            let mutable s_exp = []
            let mutable s_errs = []
            let mutable s_auth = []
            let mutable s_time = []
            while start < en do
                let st = start
                let nxt = start.AddDays(7.0)
                let wstat = rows |> Seq.filter(fun r -> r.LogInTime >= st && r.LogOutTime < nxt) |> List.ofSeq
                let totalCount = wstat |> Seq.length
                let unique = wstat |> Seq.countBy(fun r -> r.PartitionKey) |> Seq.length
                let x = wstat |> List.map (fun r -> r.FurtherTestingCount + r.RunProofCount + r.RunSimulationCount) |> Seq.sum
                let a = wstat |> List.map (fun r -> r.ImportModelCount + r.NewModelCount + r.SaveModelCount) |> Seq.sum
                let e = wstat |> List.map (fun r -> r.ProofErrorCount + r.SimulationErrorCount + r.FurtherTestingErrorCount) |> Seq.sum       
                let tm = wstat |> List.map (fun r -> (r.LogOutTime - r.LogInTime).TotalMinutes) |> Seq.sum 
                writer.WriteLine(sprintf "%A,%d,%d,%d,%d,%d,%f" st totalCount unique x a e tm)
                s_sessions <- (st, totalCount) :: s_sessions
                s_unique <- (st, unique) :: s_unique
                s_exp <- (st, x) :: s_exp
                s_errs <- (st, e) :: s_errs
                s_auth <- (st, a) :: s_auth
                s_time <- (st, tm) :: s_time
                start <- nxt
          
            let form = new Form(Visible = true, Text = "BMA client usage statistics")
            let table = new TableLayoutPanel(Dock = DockStyle.Fill)
            table.ColumnCount <- 3;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.0f)) |> ignore
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.0f)) |> ignore
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34.0f)) |> ignore
            table.RowCount <- 2;
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 50.0f)) |> ignore
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 50.0f)) |> ignore

            let sessionsChart = new ChartTypes.ChartControl(Chart.Column(s_sessions, Title = "Total sessions") |> stylize, Dock = DockStyle.Fill) 
            table.Controls.Add(sessionsChart)
            let uniqChart = new ChartTypes.ChartControl(Chart.Column(s_unique, Title = "Unique users", Color = Color.DarkOliveGreen) |> stylize, Dock = DockStyle.Fill) 
            table.Controls.Add(uniqChart)
            let xChart = new ChartTypes.ChartControl(Chart.Column(s_exp, Title = "Experiment count", Color = Color.Goldenrod) |> stylize, Dock = DockStyle.Fill) 
            table.Controls.Add(xChart)
            let eChart = new ChartTypes.ChartControl(Chart.Column(s_errs, Title = "Error count", Color = Color.Red) |> stylize, Dock = DockStyle.Fill) 
            table.Controls.Add(eChart)
            let aChart = new ChartTypes.ChartControl(Chart.Column(s_auth, Title = "Authoring operations", Color = Color.BlueViolet) |> stylize, Dock = DockStyle.Fill) 
            table.Controls.Add(aChart)
            let tChart = new ChartTypes.ChartControl(Chart.Column(s_time, Title = "Total time (minutes)", Color = Color.Chocolate) |> stylize, Dock = DockStyle.Fill) 
            table.Controls.Add(tChart)
            table.SetColumn(uniqChart, 1)
            table.SetColumn(xChart, 2)
            table.SetRow(eChart, 1)
            table.SetRow(aChart, 1)
            table.SetColumn(aChart, 1)
            table.SetRow(tChart, 1)
            table.SetColumn(tChart, 2)
            form.Controls.Add(table)
            form.WindowState <- FormWindowState.Maximized
            Application.Run(form);
        else
            printfn "There is no 'ClientActivity' table in the given storage account"
        0 // return an integer exit code
