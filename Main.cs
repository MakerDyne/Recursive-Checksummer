/*
Author: Richard Leszczynski
Email: richard@makerdyne.com
Website: http://www.MakerDyne.com
*/

using System;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;


namespace RecursiveChecksummer
{
	class MainClass
	{
		public static int Main(string[] args)
		{
			// Source and destination root directories
			string rootSource = null;
			string rootDest = null;
			string fileWithChecksums = null;
			bool createFileWithChecksums = false;
			int numCores = Environment.ProcessorCount;
			StreamWriter fwcWriter;
			StreamReader fwcReader;
			ushort programMode = 0;
			bool useDotNetMD5 = false;
			bool printDebugInfo = false;			
			// Source
			Stopwatch sourceTimer = new Stopwatch();
			uint numSourceFiles = 0;
			uint numSourceDirs = 1;
			ConcurrentBag<string> sourceFilesToProcess = new ConcurrentBag<string>();
			ConcurrentDictionary<string, string> sourceFilesWithChecksums = new ConcurrentDictionary<string, string>(numCores,1000);
			ConcurrentBag<string> sourceFilesWithoutChecksums = new ConcurrentBag<string>();	// When there's a problem generating a checksum
			SortedList<string, string> sortedSourceFilesWithChecksums = new SortedList<string, string>();
			SortedSet<string> sortedSourceFilesWithoutChecksums = new SortedSet<string>();
			// Destination
			Stopwatch destTimer = new Stopwatch();
			uint numDestFiles = 0;
			uint numDestDirs = 1;
			ConcurrentBag<string> destFilesToProcess = new ConcurrentBag<string>();
			ConcurrentDictionary<string, string> destFilesWithChecksums = new ConcurrentDictionary<string, string>(numCores,1000);
			ConcurrentBag<string> destFilesWithoutChecksums = new ConcurrentBag<string>(); 	// When there's a problem generating a checksum
			SortedList<string, string> sortedDestFilesWithChecksums = new SortedList<string, string>();
			SortedSet<string> sortedDestFilesWithoutChecksums = new SortedSet<string>();
			// Difference
			ConcurrentBag<string> filesNoMatch = new ConcurrentBag<string>();
			ConcurrentBag<string> filesInSourceNotDest = new ConcurrentBag<string>();
			ConcurrentBag<string> filesInDestNotSource = new ConcurrentBag<string>();
			SortedSet<string> sortedFilesNoMatch = new SortedSet<string>();
			SortedSet<string> sortedFilesInSourceNotDest = new SortedSet<string>();
			SortedSet<string> sortedFilesInDestNotSource = new SortedSet<string>();
			
			/* PROCESS COMMAND LINE ARGUMENTS
			 * Command line flags:
			 * -s = root source directory
			 * -d = root destination directory
			 * -f = path to a file containing checksums of a set of files to check
			 * -c = number of processor cores to use for checksum generation
			 * -n = use .net's built-in checksum generator instead of Linux's md5sum utility
			 * -q = print debugging information
			 * -h = print program usage details and exit
			 */
			
			for(uint i = 0; i < args.GetLength(0); i++)
			{
				switch(args[i])
				{
				case "-s":
					// signifies that the next CLA will be the root source directory
					if(rootSource != null) {
						Console.WriteLine("ERROR: multiple source (-s) directories have been specified. Please only specify a single source directory");
						printUsageInformation();
						return 1;
					}
					if(fileWithChecksums != null) {
						Console.WriteLine("ERROR: Please specify source (-s) and/or destination (-d) directories before specifying a file to hold checksums");
						printUsageInformation();
						return 1;
					}
					rootSource = args[++i];
					rootSource = rootSource.TrimEnd('/') + '/';
					if(!rootSource.StartsWith("/"))	// Deal with relative directory paths in the command line args
						rootSource = Environment.CurrentDirectory + "/" + rootSource;
					if(!Directory.Exists(rootSource)) {
						Console.WriteLine("ERROR: Source (-s) directory does not exist!");
						return 1;
					}
					break;
				case "-d":
					// signifies that the next CLA will be the root destination directory
					if(rootDest != null) {
						Console.WriteLine("ERROR: multiple destination (-d) directories have been specified. Please only specify a single destination directory");
						printUsageInformation();
						return 1;
					}
					if(fileWithChecksums != null) {
						Console.WriteLine("ERROR: Please specify source (-s) and/or destination (-d) directories before specifying a file to hold checksums");
						printUsageInformation();
						return 1;
					}
					rootDest = args[++i];
					rootDest = rootDest.TrimEnd('/') + '/';
					if(!rootDest.StartsWith("/"))	// Deal with relative directory paths in the command line args
						rootDest = Environment.CurrentDirectory + "/" + rootDest;
					if(!Directory.Exists(rootDest)) {
						Console.WriteLine("ERROR: Destination (-d) directory does not exist!");
						return 1;
					}
					break;
				case "-f":
					/* signifies that the next CLA will be a file containing checksums of files
					 * MODES OF OPERATION:
					 * 1. if -s is already specified, -f signifies a file which is to be created or overwritten to hold the checksums of all files in rootSource
					 * 2. if -s and -d are both already specified, -f MAY be used to signify a file which is to be created or overwritten to store the checksums of all
					 *    files in rootSource.
					 * 3. if -d is already specified, -f signifies an existing file which contains the checksums that all files in destsource are to be checked against
					 */
					if(fileWithChecksums != null) {
						Console.WriteLine("ERROR: multiple files to hold checksums (-f) have been specified. Please only specify a single file");
						printUsageInformation();
						return 1;
					}
					fileWithChecksums = args[++i];
					if(!fileWithChecksums.StartsWith("/"))	// Deal with relative directory paths in the command line args
						fileWithChecksums = Environment.CurrentDirectory + "/" + fileWithChecksums;
					if(!File.Exists(fileWithChecksums)) {
//						Console.WriteLine("ERROR: File to hold checksums (-f) does not exist!");
						try {
							File.Create(fileWithChecksums).Close();
						}
						catch(Exception ex) {
							Console.WriteLine("ERROR: Problem creating file to hold checksums");
							Console.WriteLine(ex.Message);
						}
					}
					break;
				case "-c":
					// determines the number of concurrent instances of md5sum to run. Cannot be greater than the number of processor cores
					ushort numCoresAvailable = (ushort)Environment.ProcessorCount;
					if(numCoresAvailable<1)
						numCoresAvailable = 1;
					try {
						numCores = Convert.ToUInt16(args[++i]);
					}
					catch(Exception ex) {
						Console.WriteLine("ERROR: Problem with the value specified for the number of cores (-c) argument: {0}. Please specify a positive integer value", args[i]);
						Console.WriteLine(ex.Message);
						return 1;
					}
					if(numCores == 0)
						numCores=1;
					else if(numCores > 2*numCoresAvailable) {
						numCores = 2*numCoresAvailable;
					}
					break;
				case "-n":
					// use .net's built-in checksum generator instead of Linux's md5sum checksum generator
					useDotNetMD5 = true;
					break;
				case "-q":
					printDebugInfo = true;
					break;
				case "-h":
					// print program usage information and exit
					printUsageInformation();
					return 0;
				default:
					// print program usage information and exit
					printUsageInformation();
					return 1;
				}
			}
			
			// Check that Linux's md5sum utility can be found
			if(!File.Exists(@"/bin/md5sum")) {
				useDotNetMD5 = true;
				Console.WriteLine("WARNING: The Linux checksum generating program (md5sum) either does not exist or cannot be found");
				Console.WriteLine("WARNING: Falling back to using .Net's built-in checksum generator");
			}
			
			// DETERMINE IF PROGRAM IS TO RUN IN MODE 1,2 or 3
			// Determine Mode 1
			if((rootSource != null) && (rootDest == null)) {
				if(fileWithChecksums != null) {
					programMode = 1;
					createFileWithChecksums = true;
				}
				else {
					Console.WriteLine("ERROR: A file to hold checksums (-f) must be specified if only -s and not -d is given as an argument");
					printUsageInformation();
					return 1;
				}
			}
			// Determine Mode 2
			else if((rootSource != null) && (rootDest != null)) {
				programMode = 2;
				// check for identical source and destination directories
				if(rootSource.TrimEnd('/') == rootDest.TrimEnd('/')) {
					Console.WriteLine("ERROR: Source (-s) and destination (-d) directories are the same. Please ensure that they are different");
					printUsageInformation();
					return 1;
				}
				createFileWithChecksums = (fileWithChecksums == null)?false:true;
			}
			// Determine Mode 3
			else if((rootSource == null) && (rootDest != null)) {
				if(fileWithChecksums != null)
					programMode = 3;
				else {
					Console.WriteLine("ERROR: A file to hold checksums (-f) must be specified if only -d and not -s is given as an argument");
					printUsageInformation();
					return 1;
				}
			}
			// Determine insufficient arguments for any mode
			else if((rootSource == null) && (rootDest == null)) {
				Console.WriteLine("Error: Neither a source (-s) nor destination (-d) directory has been specified");
				printUsageInformation();
				return 1;
			}
			
			// Create streams required for modes.
			// Do not use any encoding options when creating the StreamWriter. The Linux utility md5sum in -c mode needs a file *without* any byte-order mark inserted at the beginning
			try {
				switch(programMode) {
				case 1:
				case 2:
					if(createFileWithChecksums)
						fwcWriter = new StreamWriter(fileWithChecksums, false);
					break;
				case 3:
					fwcReader = new StreamReader(fileWithChecksums);
					break;
				default:
					Console.WriteLine("ERROR: An unidentified program mode has been selected. programMode = {0}", programMode);
					printUsageInformation();
					return 1;
				}
			}
			catch(Exception ex) {
				Console.WriteLine("ERROR: Cannot access file {0] to hold checksums in {1}",
				                  Path.GetFileName(fileWithChecksums), Path.GetDirectoryName(fileWithChecksums));
				Console.WriteLine(ex.Message);
				return 1;
			}
			// Set parallel processing options
			ParallelOptions pOpts = new ParallelOptions();
			pOpts.MaxDegreeOfParallelism = numCores;
					
			// PROGRAM EXECUTION
			// Generate list of source files to operate on
			if(programMode == 1 || programMode == 2) {
				generateFileLists(sourceFilesToProcess, rootSource, rootSource, ref numSourceFiles, ref numSourceDirs);
				sourceTimer.Start();
				Parallel.ForEach(sourceFilesToProcess, pOpts, currentFile => {
					createChecksum(rootSource, currentFile, sourceFilesWithChecksums, sourceFilesWithoutChecksums, useDotNetMD5);
				}); // end Parallel.For
				sourceTimer.Stop();

				// Sort and store the list of files with checksums to a file
				foreach(string file in sourceFilesWithChecksums.Keys) {
					sortedSourceFilesWithChecksums.Add(file, sourceFilesWithChecksums[file]);
				}
				foreach(string file in sourceFilesWithoutChecksums) {
					sortedSourceFilesWithoutChecksums.Add(file);
				}
				if(sortedSourceFilesWithoutChecksums.Count != 0) {
					Console.WriteLine("ERROR: There were files within the source directory tree for which checksums could not be generated. It is likely the program did not have sufficient privileges to read these files");
					foreach(string file in sortedSourceFilesWithoutChecksums) {
						Console.WriteLine("Could not create checksum for file in source: {0}", file);
					}
					return 1;
				}
			}

			if(programMode == 3) {
				// read contents of fileWithChecksums into sourceFilesWithChecksums
				string line = null;
				string checksum = null;
				string file = null;
				int doubleSpacePos = 0;
				while((line = fwcReader.ReadLine()) != null) {
					// extract the checksum from the line (successful md5sum output should be 1 line: checksum followed by two spaces and the relative file-path)
					checksum = null;
					file = null;
					doubleSpacePos = line.IndexOf("  ");
					if(doubleSpacePos == -1) {
						Console.WriteLine("ERROR: Problem reading checksums and filenames from {0}. Could not find double whitespace separator between checksum and filename", fileWithChecksums);
						// TODO: decide whether to return 1 or add the offending file to sourceFilesWithoutChecksums
						// NB: there may not be a filename if there's no double space found...
						// Try adding the whole line to filesWithoutChecksums, so the user can see the unedited problematic lines
						return 1;
					}
					try {
						checksum = line.Remove(doubleSpacePos);
						file = line.Substring(line.IndexOf("  ")+2);
					}
					catch(ArgumentOutOfRangeException ex) {
						Console.WriteLine("ERROR: Problem reading checksums and filenames from {0}. Could not separate out checksum and filename", fileWithChecksums);
						fwcReader.Close();
						return 1;
					}
					if(!sourceFilesWithChecksums.TryAdd(file, checksum)) { // NB: note the use of *source*FilesWithChecksums to hold the file contents
						Console.WriteLine("ERROR: Problem parsing the file with checksums {0}, it contains duplicate lines", fileWithChecksums);
						return 1;
					}
				}
				fwcReader.Close();
				foreach(string fileWithChecksum in sourceFilesWithChecksums.Keys)
					sortedSourceFilesWithChecksums.Add(fileWithChecksum, sourceFilesWithChecksums[fileWithChecksum]);
			}
			// Generate list of destination files to operate on
			if(programMode == 2 || programMode == 3) {
				generateFileLists(destFilesToProcess, rootDest, rootDest, ref numDestFiles, ref numDestDirs);
				destTimer.Start();
				Parallel.ForEach(destFilesToProcess, pOpts, currentFile => {
					createChecksum(rootDest, currentFile, destFilesWithChecksums, destFilesWithoutChecksums, useDotNetMD5);
				}); // end Parallel.For
				destTimer.Stop();
				
				// Sort and store the list of files with checksums to a file
				foreach(string file in destFilesWithChecksums.Keys) {
					sortedDestFilesWithChecksums.Add(file, destFilesWithChecksums[file]);
				}
				foreach(string file in destFilesWithoutChecksums) {
					sortedDestFilesWithoutChecksums.Add(file);
				}
				if(sortedDestFilesWithoutChecksums.Count != 0) {
					Console.WriteLine("ERROR: There were files within the destination directory tree for which checksums could not be generated. It is likely the program did not have sufficient privileges to read these files");
					foreach(string file in sortedDestFilesWithoutChecksums) {
						Console.WriteLine("Could not create checksum for file in destination: {0}", file);
					}
					return 1;
				}
			}
		
			// the writing of -f has been deliberately moved to after all processing has finished on the source and destination directories 
			if(programMode == 1 || programMode == 2) {
				if(createFileWithChecksums) {
					foreach(string file in sortedSourceFilesWithChecksums.Keys)
						fwcWriter.WriteLine(string.Format("{0}  {1}", sortedSourceFilesWithChecksums[file], file));
					fwcWriter.Close();
				}
			}
			
			// Compare the source and destination file lists
			// Find files in both source and destination but which have different checksums
			foreach(string file in sortedSourceFilesWithChecksums.Keys) {
				if(sortedDestFilesWithChecksums.ContainsKey(file)) {
					if(sortedSourceFilesWithChecksums[file] != sortedDestFilesWithChecksums[file])
						filesNoMatch.Add(file);
				}
				else // file is in source but not destination
					filesInSourceNotDest.Add(file);
			}
			// Find files in destination but not source
			foreach(string file in sortedDestFilesWithChecksums.Keys) {
				if(!sortedSourceFilesWithChecksums.ContainsKey(file))
					filesInDestNotSource.Add(file);
			}
			
			// TODO: Compare file sizes of source and destination files before computing checksums for them
			// TODO: Store file sizes as well as checksums in the -f file/ - like cksum outputs: sum size filepath
			// TODO: Compare speed of md5sum with .NET's own cryptographic functions
			// TODO: Implement stopwatch
			// TODO: Implement verbose mode that outputs progress information to console if a -v command line arg is given
			// 		 Stage of process: generating source/dest file lists, generating source/dest checksums, reading/writing file with checksums, sorting source/dest/match file lists
			
			// RESULTS OF COMPARISON
			Console.WriteLine("\n------------------------------------------------------------------------------");
			Console.WriteLine("RecursiveChecksummer: Results of source and destination directory comparison:");
			Console.WriteLine("------------------------------------------------------------------------------\n");
			
			// TODO: Delete this little duplicate bit once benchmarking is complete
			Console.WriteLine("Number of processor cores requested is {0}", numCores);
			if(useDotNetMD5)
				Console.WriteLine("Using .Net's built-in checksum generator");
			else
				Console.WriteLine("Using Linux's md5sum checksum generator");
			Console.WriteLine();
			
			// DEVELOPMENT INFORMATION SUMMARY
			if(printDebugInfo)
			{
				Console.WriteLine();
				Console.WriteLine("Program mode is {0}", programMode);
				Console.WriteLine("Number of processor cores requested is {0}", numCores);
				if(useDotNetMD5)
					Console.WriteLine("Using .Net's built-in checksum generator");
				else
					Console.WriteLine("Using Linux's md5sum checksum generator");
				Console.WriteLine();
				Console.WriteLine("Number of files in sourceFilesToProcess is {0}", sourceFilesToProcess.Count);
				Console.WriteLine("Number of files in sourceFilesWithChecksums is {0}", sourceFilesWithChecksums.Count);
				Console.WriteLine("Number of files in sourceFilesWithoutChecksums is {0}", sourceFilesWithoutChecksums.Count);
				Console.WriteLine("Number of files in sortedSourceFilesWithChecksums is {0}", sortedSourceFilesWithChecksums.Count);
				Console.WriteLine("Number of files in sortedSourceFilesWithoutChecksums is {0}", sortedSourceFilesWithoutChecksums.Count);
				Console.WriteLine();
				Console.WriteLine("Number of files in destFilesToProcess is {0}", destFilesToProcess.Count);
				Console.WriteLine("Number of files in destFilesWithChecksums is {0}", destFilesWithChecksums.Count);
				Console.WriteLine("Number of files in destFilesWithoutChecksums is {0}", destFilesWithoutChecksums.Count);
				Console.WriteLine("Number of files in sortedDestFilesWithChecksums is {0}", sortedDestFilesWithChecksums.Count);
				Console.WriteLine("Number of files in sortedDestFilesWithoutChecksums is {0}", sortedDestFilesWithoutChecksums.Count);
				Console.WriteLine();
				Console.WriteLine("Number of files in filesNoMatch is {0}", filesNoMatch.Count);
				Console.WriteLine("Number of files in filesInSourceNotDest is {0}", filesInSourceNotDest.Count);
				Console.WriteLine("Number of files in filesInDestNotSource is {0}", filesInDestNotSource.Count);
				Console.WriteLine("Number of files in sortedFilesNoMatch is {0}", sortedFilesNoMatch.Count);
				Console.WriteLine("Number of files in sortedFilesInSourceNotDest is {0}", sortedFilesInSourceNotDest.Count);
				Console.WriteLine("Number of files in sortedFilesInDestNotSource is {0}", sortedFilesInDestNotSource.Count);
				Console.WriteLine();
			}
			
			if(programMode == 1 || programMode == 2) {
				if(fileWithChecksums != null)
					Console.WriteLine("An md5sum -c compatible file holding the checksums of all the files in the source directory has been created at\n{0}\n", fileWithChecksums);
				Console.WriteLine("Source directory: {0}", rootSource);
				Console.WriteLine("Source contained {0} files in {1} directories", numSourceFiles, numSourceDirs);
				Console.WriteLine("Source directory checksums generated in {0} seconds", sourceTimer.Elapsed.TotalSeconds);
				Console.WriteLine("Source directory checksums generated in {0}:{1}:{2} (h:m:s)\n", sourceTimer.Elapsed.Hours, sourceTimer.Elapsed.Minutes, sourceTimer.Elapsed.Seconds);
			}
			if(programMode == 2 || programMode == 3) {
				Console.WriteLine("Destination directory: {0}", rootDest);
				Console.WriteLine("Destination contained {0} files in {1} directories", numDestFiles, numDestDirs);
				Console.WriteLine("Destination directory checksums generated in {0} seconds", destTimer.Elapsed.TotalSeconds);
				Console.WriteLine("Destination directory checksums generated in {0}:{1}:{2} (h:m:s)\n", destTimer.Elapsed.Hours, destTimer.Elapsed.Minutes, destTimer.Elapsed.Seconds);
				if((filesNoMatch.Count == 0) && (filesInSourceNotDest.Count == 0) && (filesInDestNotSource.Count == 0)) {
					Console.WriteLine("SUCCESS: No differences found between source and destination directories\n");
				}
				else {
					Console.WriteLine("WARNING: The following differences were found between the source and destination directories\n");
					if(filesNoMatch.Count != 0) {
						Console.WriteLine("The following {0} files are different between the source and destination directories", filesNoMatch.Count);
						foreach(string file in filesNoMatch)
							Console.WriteLine(file);
						Console.WriteLine();
					}
					if(filesInSourceNotDest.Count != 0) {
						Console.WriteLine("The following {0} files were present in the source directories but not in the destination directories:", filesInSourceNotDest.Count);
						foreach(string file in (filesInSourceNotDest))
							Console.WriteLine(file);
						Console.WriteLine();
					}
					if(filesInDestNotSource.Count != 0) {
						Console.WriteLine("The following {0} files were present in the destination directories but not in the source directories:", filesInDestNotSource.Count);
						foreach(string file in (filesInDestNotSource))
							Console.WriteLine(file);
						Console.WriteLine();
					}
				}
			}
			return 0;
		}
		
		// Function to generate lists of files to be passed to the checksumming program (md5sum) (function used for Mode 1 and 2)
		static void generateFileLists(ConcurrentBag<string> fileList, string rootDir, string currentDir, ref uint fileCounter, ref uint dirCounter)
		{
			// TODO: resolve problem of /proc /sys /dev and other unwelcome directories
			string[] files = Directory.GetFiles(currentDir);
			string[] dirs = Directory.GetDirectories(currentDir);
			foreach(string file in files) {
				fileList.Add(file.Replace(rootDir,""));
				++fileCounter;
			}
			foreach(string dir in dirs) {
				++dirCounter;
				generateFileLists(fileList, rootDir, dir, ref fileCounter, ref dirCounter);
			}
		}

		static void createChecksum(string workingDir, string file, ConcurrentDictionary<string, string> successList, ConcurrentBag<string> failureList, bool useDotNet)
		{
			if(useDotNet) {	// use .Net's own cryptographic functions to generate a checksum
				byte[] checksumArray;
				string checksum;
				try {
					FileStream fileToOpen = new FileStream(workingDir+file, FileMode.Open,  FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);	// TODO: fine-tune buffersize
					MD5 md5 = new MD5CryptoServiceProvider();
					checksumArray = md5.ComputeHash(fileToOpen);
					fileToOpen.Close();
					checksum = (BitConverter.ToString(checksumArray)).Replace("-","").ToLower();
					successList.AddOrUpdate(file, checksum, (sKey, sVal) => checksum);
				}
				catch(Exception ex) {
					failureList.Add(file);
					Console.WriteLine(ex.Message);
				}	
			}
			else {	// use the Linux utility md5sum
				string commandArgs = String.Format("\"{0}\"", file);
				string command = "md5sum";
				
				ProcessStartInfo procSettings = new ProcessStartInfo();
				procSettings.FileName = command;
				procSettings.Arguments = commandArgs;
				procSettings.CreateNoWindow = true;
				procSettings.UseShellExecute = false;
				procSettings.WorkingDirectory = workingDir;
				procSettings.RedirectStandardOutput = true;
				try {
					using(Process checksummer = Process.Start(procSettings)) {
						using(StreamReader checksummerOutput = checksummer.StandardOutput) {
							// process the output
							checksummer.WaitForExit();
							string outputLine;
							if(checksummer.ExitCode == 0 && (outputLine = checksummerOutput.ReadLine()) != null) {
								// extract the checksum from the line (successful md5sum output should be 1 line: checksum followed by two spaces and the relative file-path)
								string checksum = outputLine.Remove(outputLine.IndexOf(' '));
								successList.AddOrUpdate(file, checksum, (sKey, sVal) => checksum); // TODO: decide whether to use AddOrUpdate or Add method, and why?
							}
							else {
								failureList.Add(file);
							}
						}
					}
				}
				catch(Exception ex) {
					Console.WriteLine("ERROR: Problem running md5sum");
					Console.WriteLine(ex.Message);
				}
			}
		}

		static void printUsageInformation()
		{
			Console.WriteLine("\n------------------------------------------------------------------------------");
			Console.WriteLine("Recursive Checksummer");
			Console.WriteLine("Program to generate or check MD5 checksums of all files within a directory tree");
			Console.WriteLine("------------------------------------------------------------------------------\n");
			Console.WriteLine("Usage Summary:\n");
			Console.WriteLine("Program runs in one of three modes:\n\n" +
				"1.) Given a source directory, generates checksums for all files within that directory tree and writes them to a file\n" +
			    "    Required command line arguments for this mode: -s -f\n\n" +
				"2.) Given a source and destination directory, compares checksums for all files within the source tree with those in the destination tree.\n" +
				"    Optionally will also write the checksums for all files within the source directory to a file.\n" +
			    "    Required command line arguments for this mode: -s -d, Optional: -f\n\n" +
                "3.) Given a destination directory, checks checksums for all files within that directory against a list provided from a file.\n" +
				"    Required command line arguments for this mode: -d -f\n\n");
			Console.WriteLine("Command line argument details:\n\n" +
				"-s\t/path/to/source/directory\n\n" +
				"-d\t/path/to/destination/directory\n\n" +
				"-f\t/path/to/fileWithChecksums.txt - Must be spedified after (-s) and/or (-d).\n" +
				"\tIf (-s) is specified, checksums will be written to the file. If the file already exists, it will be overwritten without warning.\n" +
				"\tIf (-d) is specified, checksums will be read from the file.\n" +
			    "\tIf the file is within either the source or destination directory trees, it will be ignored in the checksumming process.\n\n" +
			    "-c\t(Optional) Number of processor cores to use when calculating checksums. Specify a positive integer number.\n\n" +
			    "-n\t(Optional) Use .Net's built-in MD5 checksum generator instead of the Linux utility md5sum.\n" +
			    "\tIf md5sum cannot be found, program will automatically fall back to using .Net for checksum generation.\n" +
			    "\t.Net's checksum generator is slower than the md5sum utility.\n\n" +
			    "-q\t(Optional) Print debugging information in the program output.\n\n" +
			    "-h\t(Optional) Print program usage information and exit.\n");
		}
	}
}
