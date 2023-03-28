namespace MediaToolkit
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;

    using MediaToolkit.Model;
    using MediaToolkit.Options;
    using MediaToolkit.Properties;
    using MediaToolkit.Util;

    /// -------------------------------------------------------------------------------------------------
    /// <summary>   An engine. This class cannot be inherited. </summary>
    public class Engine : EngineBase
    {

        /// <summary>
        ///     Event queue for all listeners interested in conversionComplete events.
        /// </summary>
        public event EventHandler<ConversionCompleteEventArgs> ConversionCompleteEvent;

        public Engine()
        {
            
        }

        public Engine(string ffMpegPath) : base(ffMpegPath)
        {
            
        }

        /// -------------------------------------------------------------------------------------------------
        /// <summary>
        ///     <para> ---</para>
        ///     <para> Converts media with conversion options</para>
        /// </summary>
        /// <param name="inputFile">    Input file. </param>
        /// <param name="outputFile">   Output file. </param>
        /// <param name="options">      Conversion options. </param>
        public Task Convert(MediaFile inputFile, MediaFile outputFile, ConversionOptions options)
        {
            EngineParameters engineParams = new EngineParameters
                {
                    InputFile = inputFile,
                    OutputFile = outputFile,
                    ConversionOptions = options,
                    Task = FFmpegTask.Convert
                };

            return this.FFmpegEngine(engineParams);
        }

        /// -------------------------------------------------------------------------------------------------
        /// <summary>
        ///     <para> ---</para>
        ///     <para> Converts media with default options</para>
        /// </summary>
        /// <param name="inputFile">    Input file. </param>
        /// <param name="outputFile">   Output file. </param>
        public Task Convert(MediaFile inputFile, MediaFile outputFile)
        {
            EngineParameters engineParams = new EngineParameters
                {
                    InputFile = inputFile,
                    OutputFile = outputFile,
                    Task = FFmpegTask.Convert
                };

            return this.FFmpegEngine(engineParams);
        }

        /// <summary>   Event queue for all listeners interested in convertProgress events. </summary>
        public event EventHandler<ConvertProgressEventArgs> ConvertProgressEvent;

        public void CustomCommand(string ffmpegCommand)
        {
            if (ffmpegCommand.IsNullOrWhiteSpace())
            {
                throw new ArgumentNullException("ffmpegCommand");
            }

            EngineParameters engineParameters = new EngineParameters { CustomArguments = ffmpegCommand };

            this.StartFFmpegProcess(engineParameters).Wait();
        }

        /// -------------------------------------------------------------------------------------------------
        /// <summary>
        ///     <para> Retrieve media metadata</para>
        /// </summary>
        /// <param name="inputFile">    Retrieves the metadata for the input file. </param>
        public Task GetMetadata(MediaFile inputFile)
        {
            EngineParameters engineParams = new EngineParameters
                {
                    InputFile = inputFile,
                    Task = FFmpegTask.GetMetaData
                };

            return this.FFmpegEngine(engineParams);
        }

        /// -------------------------------------------------------------------------------------------------
        /// <summary>   Retrieve a thumbnail image from a video file. </summary>
        /// <param name="inputFile">    Video file. </param>
        /// <param name="outputFile">   Image file. </param>
        /// <param name="options">      Conversion options. </param>
        public Task GetThumbnail(MediaFile inputFile, MediaFile outputFile, ConversionOptions options)
        {
            EngineParameters engineParams = new EngineParameters
                {
                    InputFile = inputFile,
                    OutputFile = outputFile,
                    ConversionOptions = options,
                    Task = FFmpegTask.GetThumbnail
                };

            return this.FFmpegEngine(engineParams);
        }
        
        #region Private method - Helpers

        private Task FFmpegEngine(EngineParameters engineParameters)
        {
            if (!engineParameters.InputFile.Filename.StartsWith("http://") && !File.Exists(engineParameters.InputFile.Filename))
            {
                throw new FileNotFoundException(Resources.Exception_Media_Input_File_Not_Found, engineParameters.InputFile.Filename);
            }

            return this.StartFFmpegProcess(engineParameters);
        }

        private ProcessStartInfo GenerateStartInfo(EngineParameters engineParameters)
        {
            string arguments = CommandBuilder.Serialize(engineParameters);

            return this.GenerateStartInfo(arguments);
        }

        private ProcessStartInfo GenerateStartInfo(string arguments)
        {
            //windows case
            if (Path.DirectorySeparatorChar == '\\')
            {
                return new ProcessStartInfo
                {
                    Arguments = "-nostdin -y -loglevel info " + arguments,
                    FileName = this.FFmpegFilePath,
                    CreateNoWindow = true,
                    RedirectStandardInput = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
            }
            else //linux case: -nostdin options doesn't exist at least in debian ffmpeg
            {
                return new ProcessStartInfo
                {
                    Arguments = "-y -loglevel info " + arguments,
                    FileName = this.FFmpegFilePath,
                    CreateNoWindow = true,
                    RedirectStandardInput = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
            }
        }
        
        #endregion

        /// -------------------------------------------------------------------------------------------------
        /// <summary>   Raises the conversion complete event. </summary>
        /// <param name="e">    Event information to send to registered event handlers. </param>
        private void OnConversionComplete(ConversionCompleteEventArgs e)
        {
            EventHandler<ConversionCompleteEventArgs> handler = this.ConversionCompleteEvent;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        /// -------------------------------------------------------------------------------------------------
        /// <summary>   Raises the convert progress event. </summary>
        /// <param name="e">    Event information to send to registered event handlers. </param>
        private void OnProgressChanged(ConvertProgressEventArgs e)
        {
            EventHandler<ConvertProgressEventArgs> handler = this.ConvertProgressEvent;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        /// <summary>
        /// Run process result
        /// </summary>
        public class ProcessHelperResult
        {
            /// <summary>
            /// Exit code
            /// <para>If NULL, process exited due to timeout</para>
            /// </summary>
            public int? ExitCode { get; set; } = null;

            /// <summary>
            /// Standard error stream
            /// </summary>
            public string StdErr { get; set; } = "";

            /// <summary>
            /// Standard output stream
            /// </summary>
            public string StdOut { get; set; } = "";

            public Exception exp { get; set; } = null;

            public TimeSpan totalMediaDuration = new TimeSpan();
        }

        private async Task<ProcessHelperResult> Run(Engine engine, EngineParameters engineParameters, ProcessStartInfo startInfo, string stdIn = null, int? timeoutMs = null)
        {
            

            var result = new ProcessHelperResult();

            if (!string.IsNullOrWhiteSpace(stdIn))
            {
                startInfo.RedirectStandardInput = true;
            }

            using (var process = new Process() { StartInfo = startInfo, EnableRaisingEvents = true })
            {
                // List of tasks to wait for a whole process exit
                List<Task> processTasks = new List<Task>();

                var stdErrProcessGotExceptionEvent = new TaskCompletionSource<bool>();

                // === EXITED Event handling ===
                var processExitEvent = new TaskCompletionSource<object>();
                process.Exited += (sender, args) =>
                {
                    processExitEvent.SetResult(true);
                };
                processTasks.Add(processExitEvent.Task);

                // === STDOUT handling ===
                var stdOutBuilder = new StringBuilder();
                if (process.StartInfo.RedirectStandardOutput)
                {
                    var stdOutCloseEvent = new TaskCompletionSource<bool>();

                    process.OutputDataReceived += (s, e) =>
                    {
                        if (e.Data == null)
                        {
                            stdOutCloseEvent.TrySetResult(true);
                        }
                        else
                        {
                            stdOutBuilder.Append(e.Data);
                        }
                    };

                    processTasks.Add(stdOutCloseEvent.Task);
                }
                else
                {
                    // STDOUT is not redirected, so we won't look for it
                }

                // === STDERR handling ===
                var stdErrBuilder = new StringBuilder();
                if (process.StartInfo.RedirectStandardError)
                {
                    var stdErrCloseEvent = new TaskCompletionSource<bool>();

                    process.ErrorDataReceived += (s, e) =>
                    {
                        if (e.Data == null)
                        {
                            stdErrCloseEvent.TrySetResult(true);
                        }
                        else
                        {
                            stdErrBuilder.Append(e.Data);

                            try
                            {

                                if (engineParameters.InputFile != null)
                                {
                                    RegexEngine.TestVideo(e.Data, engineParameters);
                                    RegexEngine.TestAudio(e.Data, engineParameters);

                                    Match matchDuration = RegexEngine.Index[RegexEngine.Find.Duration].Match(e.Data);
                                    if (matchDuration.Success)
                                    {
                                        if (engineParameters.InputFile.Metadata == null)
                                        {
                                            engineParameters.InputFile.Metadata = new Metadata();
                                        }

                                        TimeSpan.TryParse(matchDuration.Groups[1].Value, out result.totalMediaDuration);
                                        engineParameters.InputFile.Metadata.Duration = result.totalMediaDuration;
                                    }
                                }

                                if (RegexEngine.IsProgressData(e.Data, out var progressEvent))
                                {
                                    progressEvent.TotalDuration = result.totalMediaDuration;
                                    engine.OnProgressChanged(progressEvent);
                                }
                                else if (RegexEngine.IsConvertCompleteData(e.Data, out var convertCompleteEvent))
                                {
                                    convertCompleteEvent.TotalDuration = result.totalMediaDuration;
                                    engine.OnConversionComplete(convertCompleteEvent);
                                }
                            }
                            catch (Exception ex)
                            {
                                result.exp = ex;
                                stdErrProcessGotExceptionEvent.TrySetResult(true);
                            }
                        }
                    };

                    processTasks.Add(stdErrCloseEvent.Task);
                }
                else
                {
                    // STDERR is not redirected, so we won't look for it
                }

                // === START OF PROCESS ===
                if (!process.Start())
                {
                    result.ExitCode = process.ExitCode;
                    return result;
                }

                // Read StdIn if provided
                if (process.StartInfo.RedirectStandardInput)
                {
                    using (var writer = process.StandardInput)
                    {
                        writer.Write(stdIn);
                    }
                }

                // Reads the output stream first as needed and then waits because deadlocks are possible
                if (process.StartInfo.RedirectStandardOutput)
                {
                    process.BeginOutputReadLine();
                }
                else
                {
                    // No STDOUT
                }

                if (process.StartInfo.RedirectStandardError)
                {
                    process.BeginErrorReadLine();
                }
                else
                {
                    // No STDERR
                }

                // === ASYNC WAIT OF PROCESS ===

                // Process completion = exit AND stdout (if defined) AND stderr (if defined)
                Task processCompletionTask = Task.WhenAll(processTasks);

                // Task to wait for exit OR timeout (if defined) OR got exception
                List<Task> tasks = new List<Task>
                {
                    processCompletionTask,
                    stdErrProcessGotExceptionEvent.Task
                };

                if (timeoutMs.HasValue)
                {
                    tasks.Add(Task.Delay(timeoutMs.Value));
                }

                Task<Task> awaitingTask = Task.WhenAny(tasks);

                // Let's now wait for something to end...
                if ((await awaitingTask.ConfigureAwait(false)) == processCompletionTask)
                {
                    // -> Process exited cleanly
                    result.ExitCode = process.ExitCode;
                }
                else
                {
                    // -> Timeout, let's kill the process
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                        // ignored
                    }
                }

                // Read stdout/stderr
                result.StdOut = stdOutBuilder.ToString();
                result.StdErr = stdErrBuilder.ToString();
            }

            return result;
        }



        /// -------------------------------------------------------------------------------------------------
        /// <summary>   Starts FFmpeg process. </summary>
        /// <exception cref="InvalidOperationException">
        ///     Thrown when the requested operation is
        ///     invalid.
        /// </exception>
        /// <exception cref="Exception">
        ///     Thrown when an exception error condition
        ///     occurs.
        /// </exception>
        /// <param name="engineParameters"> The engine parameters. </param>
        private async Task StartFFmpegProcess(EngineParameters engineParameters)
        {
            
            ProcessStartInfo processStartInfo = engineParameters.HasCustomArguments 
                                              ? this.GenerateStartInfo(engineParameters.CustomArguments)
                                              : this.GenerateStartInfo(engineParameters);
            var ret = await Run(this, engineParameters, processStartInfo);

            var exitCode = ret.ExitCode;
            if ((exitCode != 0 && exitCode != 1) || ret.exp != null)
            {
                throw new Exception(
                    exitCode + ": " + ret.StdErr.Substring(0, 1000),
                    ret.exp);
            }
            // using (this.FFmpegProcess = Process.Start(processStartInfo))
            // {
            //     
            //     if (this.FFmpegProcess == null)
            //     {
            //         throw new InvalidOperationException(Resources.Exceptions_FFmpeg_Process_Not_Running);
            //     }
            //
            //
            //     this.FFmpegProcess.BeginErrorReadLine();
            //     this.FFmpegProcess.WaitForExit();
            //
            //     if ((this.FFmpegProcess.ExitCode != 0 && this.FFmpegProcess.ExitCode != 1) || caughtException != null)
            //     {
            //         throw new Exception(
            //             this.FFmpegProcess.ExitCode + ": " + receivedMessagesLog[1] + receivedMessagesLog[0],
            //             caughtException);
            //     }
            // }
        }
    }
}