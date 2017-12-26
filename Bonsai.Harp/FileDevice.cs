﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Bonsai.Harp
{
    public class FileDevice : Source<HarpDataFrame>
    {
        IObservable<HarpDataFrame> source;
        readonly object captureLock = new object();
        const int ReadBufferSize = 4096;

        public FileDevice()
        {
            source = Observable.Create<HarpDataFrame>((observer, cancellationToken) =>
            {
                return Task.Factory.StartNew(() =>
                {
                    lock (captureLock)
                    {
                        using (var stream = new FileStream(FileName, FileMode.Open))
                        using (var waitSignal = new ManualResetEvent(false))
                        {
                            double timestampOffset = 0;
                            var stopwatch = new Stopwatch();

                            var harpObserver = Observer.Create<HarpDataFrame>(
                                value =>
                                {
                                    if (value.IsTimestamped)
                                    {
                                        // Packet has timestamp
                                        var seconds = BitConverter.ToUInt32(value.Message, 5);
                                        var microseconds = BitConverter.ToUInt16(value.Message, 5 + 4);
                                        double timestamp = (seconds + microseconds * 32e-6) * 1000; // ms
                                        if (!stopwatch.IsRunning)
                                        {
                                            stopwatch.Start();
                                            timestampOffset = timestamp;
                                        }

                                        var waitInterval = timestamp - timestampOffset - stopwatch.ElapsedMilliseconds;
                                        if (waitInterval > 0)
                                        {
                                            waitSignal.WaitOne((int)waitInterval);
                                        }
                                    }

                                    observer.OnNext(value);
                                },
                                observer.OnError,
                                observer.OnCompleted);
                            var transport = new StreamTransport(harpObserver);
                            transport.IgnoreErrors = IgnoreErrors;

                            int bytesToRead;
                            while (!cancellationToken.IsCancellationRequested &&
                                   (bytesToRead = Math.Min(ReadBufferSize, (int)(stream.Length - stream.Position))) > 0)
                            {
                                transport.ReceiveData(stream, ReadBufferSize, bytesToRead);
                            }
                        }
                    }
                },
                cancellationToken,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            })
            .PublishReconnectable()
            .RefCount();
        }

        [Editor("Bonsai.Design.OpenFileNameEditor, Bonsai.Design", "System.Drawing.Design.UITypeEditor, System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")]
        [Description("The path to the binary file containing harp data frames.")]
        public string FileName { get; set; }

        [Description("Indicates whether device errors should be ignored.")]
        public bool IgnoreErrors { get; set; }

        public override IObservable<HarpDataFrame> Generate()
        {
            return source;
        }
    }
}
