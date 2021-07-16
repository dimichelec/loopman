# loopman

[![GitHub](https://img.shields.io/badge/license-MIT-green)]()

loopman is an open source guitar looper written by [Carmen DiMichele](https://dimichelec.wixsite.com/carmendimichele) 

This tiny Windows WPF app implements a simple looper that can record a short audio input and play it back in a continuous loop. I use this when practicing guitar and writing music. With it, I can quickly lay down a chord progression have it loop and solo over it when practicingor writing rhythms and scales. It's faster than doing the same thing in my DAW, which I go to after I've cooked the musical idea a bit on the looper.

There are plenty of hardware loopers on the market that are great for using this and in performing. Loopman is more convenient for me in lots of cases, I can customize it, and it's FREE!

It's in C# and uses Mark Heath's [NAudio](https://github.com/naudio/NAudio/blob/master/README.md)

I based the function on the ASIO driver interface. I've started adding WASAPI support, but I haven't wrung-out latencies yet.
