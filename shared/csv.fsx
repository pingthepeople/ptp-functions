#r "Microsoft.WindowsAzure.Storage"
#load "./model.fs"

namespace IgaTracker 

open System
open System.IO
open Microsoft.WindowsAzure.Storage
open Microsoft.WindowsAzure.Storage.Blob
open Model

module Csv =
    let storageContainer connStr = 
        CloudStorageAccount.Parse(connStr).CreateCloudBlobClient().GetContainerReference("artifacts")    

    let uploadBlob connStr path name =
        let container = connStr |> storageContainer
        let stream = File.OpenRead(path)
        let blob = container.GetBlockBlobReference(name)
        blob.UploadFromStream(stream)
        stream.Close()

    let downloadBlob connStr path name =
        let container = connStr |> storageContainer
        let blob = 
            container.ListBlobs()
            |> Seq.map (fun blob -> blob :?> CloudBlockBlob) 
            |> Seq.find(fun blob -> blob.Name = name)
        blob.DownloadToFile(path, FileMode.OpenOrCreate) |> ignore

    let parse value =
        match box value with
        | null -> ""
        | str -> str.ToString().Replace("\"", "\"\"")

    let formatRow b =
        let desc = sprintf "%s,\"%s\",\"%s\",\"%s\",\"%s\",%A" b.Name (parse b.Title) (parse b.Description) (parse b.Authors) (parse b.Subjects) b.OriginChamber
        let origin = sprintf "\"%s\",%s,\"%s\",%s,\"%s\",%s,\"%s\"" (parse b.OriginCommittee) (parse b.OriginCommitteeReading) (parse b.OriginCommitteeReadingVote) (parse b.OriginSecondReading) (parse b.OriginSecondReadingVote) (parse b.OriginThirdReading) (parse b.OriginThirdReadingVote)
        let crossover = sprintf "\"%s\",%s,\"%s\",%s,\"%s\",%s,\"%s\"" (parse b.CrossoverCommittee) (parse b.CrossoverCommitteeReading) (parse b.CrossoverCommitteeReadingVote) (parse b.CrossoverSecondReading) (parse b.CrossoverSecondReadingVote) (parse b.CrossoverThirdReading) (parse b.CrossoverThirdReadingVote)
        sprintf "%s,%s,%s" desc origin crossover

    let postSpreadsheet connStr name bills = 
        let header = "Bill,Title,Description,Authors,Origin Chamber,Subjects,Origin Committee,Origin Committee Reading,Vote,Second Reading,Vote,Third Reading,Vote,Crossover Committee,Crossover Committee Reading,Vote,Second Reading,Vote,Third Reading,Vote"
        let rows = bills |> Seq.map formatRow |> Seq.toList
        let tempPath = Path.GetTempFileName()
        printfn "temp path: %s" tempPath
        File.WriteAllLines(tempPath, [header] @ rows)
        uploadBlob connStr tempPath name
        File.Delete(tempPath)

    let generateAllBillsSpreadsheetFilename() = sprintf "leg-update-all-%s.csv" (datestamp())
    let generateUserBillsSpreadsheetFilename userId = sprintf "leg-update-%d-%s.csv" userId (datestamp())