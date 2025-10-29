// test some of the internal Kcp.cs functions to guarantee stability.
using NUnit.Framework;

namespace kcp2k.Tests
{
    public class KcpTests
    {
        [Test]
        public void InsertSegmentInReceiveBuffer()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // insert '1' should insert into empty buffer
            Segment one = new Segment{sn=1};
            kcp.InsertSegmentInReceiveBuffer(one);
            Assert.That(kcp.rcv_buf.Count, Is.EqualTo(1));
            Assert.That(kcp.rcv_buf[0], Is.EqualTo(one));

            // insert '3' should insert after '1'
            Segment three = new Segment{sn=3};
            kcp.InsertSegmentInReceiveBuffer(three);
            Assert.That(kcp.rcv_buf.Count, Is.EqualTo(2));
            Assert.That(kcp.rcv_buf[0], Is.EqualTo(one));
            Assert.That(kcp.rcv_buf[1], Is.EqualTo(three));

            // insert '2' should insert before '3'
            Segment two = new Segment{sn=2};
            kcp.InsertSegmentInReceiveBuffer(two);
            Assert.That(kcp.rcv_buf.Count, Is.EqualTo(3));
            Assert.That(kcp.rcv_buf[0], Is.EqualTo(one));
            Assert.That(kcp.rcv_buf[1], Is.EqualTo(two));
            Assert.That(kcp.rcv_buf[2], Is.EqualTo(three));

            // insert '0' should insert before '1'
            Segment zero = new Segment{sn=0};
            kcp.InsertSegmentInReceiveBuffer(zero);
            Assert.That(kcp.rcv_buf.Count, Is.EqualTo(4));
            Assert.That(kcp.rcv_buf[0], Is.EqualTo(zero));
            Assert.That(kcp.rcv_buf[1], Is.EqualTo(one));
            Assert.That(kcp.rcv_buf[2], Is.EqualTo(two));
            Assert.That(kcp.rcv_buf[3], Is.EqualTo(three));

            // insert '2' again should do nothing because duplicate
            Segment two_again = new Segment{sn=2};
            kcp.InsertSegmentInReceiveBuffer(two_again);
            Assert.That(kcp.rcv_buf.Count, Is.EqualTo(4));
            Assert.That(kcp.rcv_buf[0], Is.EqualTo(zero));
            Assert.That(kcp.rcv_buf[1], Is.EqualTo(one));
            Assert.That(kcp.rcv_buf[2], Is.EqualTo(two));
            Assert.That(kcp.rcv_buf[3], Is.EqualTo(three));
        }

        [Test]
        public void ParseAckFirst()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // insert three segments into send buffer
            Segment one = new Segment{sn=1};
            Segment two = new Segment{sn=2};
            Segment three = new Segment{sn=3};
            kcp.snd_buf.Add(one);
            kcp.snd_buf.Add(two);
            kcp.snd_buf.Add(three);

            // parse ack only removes if sn < snd_nxt
            kcp.snd_nxt = 999;

            // parse ack with sn == 3, should remove the last segment
            kcp.ParseAck(1);
            Assert.That(kcp.snd_buf.Count, Is.EqualTo(2));
            Assert.That(kcp.snd_buf[0], Is.EqualTo(two));
            Assert.That(kcp.snd_buf[1], Is.EqualTo(three));
        }

        [Test]
        public void ParseAckMiddle()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // insert three segments into send buffer
            Segment one = new Segment{sn=1};
            kcp.snd_buf.Add(one);
            Segment two = new Segment{sn=2};
            kcp.snd_buf.Add(two);
            Segment three = new Segment{sn=3};
            kcp.snd_buf.Add(three);

            // parse ack only removes if sn < snd_nxt
            kcp.snd_nxt = 999;

            // parse ack with sn == 2, should remove the middle segment
            kcp.ParseAck(2);
            Assert.That(kcp.snd_buf.Count, Is.EqualTo(2));
            Assert.That(kcp.snd_buf[0], Is.EqualTo(one));
            Assert.That(kcp.snd_buf[1], Is.EqualTo(three));
        }

        [Test]
        public void ParseAckLast()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // insert three segments into send buffer
            Segment one = new Segment{sn=1};
            kcp.snd_buf.Add(one);
            Segment two = new Segment{sn=2};
            kcp.snd_buf.Add(two);
            Segment three = new Segment{sn=3};
            kcp.snd_buf.Add(three);

            // parse ack only removes if sn < snd_nxt
            kcp.snd_nxt = 999;

            // parse ack with sn == 3, should remove the last segment
            kcp.ParseAck(3);
            Assert.That(kcp.snd_buf.Count, Is.EqualTo(2));
            Assert.That(kcp.snd_buf[0], Is.EqualTo(one));
            Assert.That(kcp.snd_buf[1], Is.EqualTo(two));
        }

        [Test]
        public void ParseAckSndNxtSmaller()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // insert three segments into send buffer
            Segment one = new Segment{sn=1};
            kcp.snd_buf.Add(one);
            Segment two = new Segment{sn=2};
            kcp.snd_buf.Add(two);
            Segment three = new Segment{sn=3};
            kcp.snd_buf.Add(three);

            // parse ack only removes if sn < snd_nxt.
            // it should do nothing if snd_nxt is <= sn
            kcp.snd_nxt = 1;

            // parse ack should not remove anything
            kcp.ParseAck(1);
            Assert.That(kcp.snd_buf.Count, Is.EqualTo(3));
            Assert.That(kcp.snd_buf[0], Is.EqualTo(one));
            Assert.That(kcp.snd_buf[1], Is.EqualTo(two));
            Assert.That(kcp.snd_buf[2], Is.EqualTo(three));
        }

        // test with empty buffer
        [Test]
        public void ParseUna_Empty()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // parse_una should remove all segments < una from send buffer
            kcp.ParseUna(2);
            Assert.That(kcp.snd_buf.Count, Is.EqualTo(0));
        }

        // test where no elements should be removed
        [Test]
        public void ParseUna_None()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // insert three segments into send buffer
            Segment one = new Segment{sn=1};
            kcp.snd_buf.Add(one);
            Segment two = new Segment{sn=2};
            kcp.snd_buf.Add(two);
            Segment three = new Segment{sn=3};
            kcp.snd_buf.Add(three);

            // parse_una should remove all segments < una from send buffer
            kcp.ParseUna(1);
            Assert.That(kcp.snd_buf.Count, Is.EqualTo(3));
            Assert.That(kcp.snd_buf[0], Is.EqualTo(one));
            Assert.That(kcp.snd_buf[1], Is.EqualTo(two));
            Assert.That(kcp.snd_buf[2], Is.EqualTo(three));
        }

        // test where some elements should be removed
        [Test]
        public void ParseUna_Some()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // insert three segments into send buffer
            Segment one = new Segment{sn=1};
            kcp.snd_buf.Add(one);
            Segment two = new Segment{sn=2};
            kcp.snd_buf.Add(two);
            Segment three = new Segment{sn=3};
            kcp.snd_buf.Add(three);

            // parse_una should remove all segments < una from send buffer
            kcp.ParseUna(2);
            Assert.That(kcp.snd_buf.Count, Is.EqualTo(2));
            Assert.That(kcp.snd_buf[0], Is.EqualTo(two));
            Assert.That(kcp.snd_buf[1], Is.EqualTo(three));
        }

        // test where all elements should be removed
        [Test]
        public void ParseUna_All()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // insert three segments into send buffer
            Segment one = new Segment{sn=1};
            kcp.snd_buf.Add(one);
            Segment two = new Segment{sn=2};
            kcp.snd_buf.Add(two);
            Segment three = new Segment{sn=3};
            kcp.snd_buf.Add(three);

            // parse_una should remove all segments < una from send buffer
            kcp.ParseUna(4);
            Assert.That(kcp.snd_buf.Count, Is.EqualTo(0));
        }

        // test with no elements in send buffer
        [Test]
        public void ParseFastack_Empty()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            kcp.snd_una = 1; // == sn to ensure <= is checked
            kcp.snd_nxt = 2; // sn + 1 to ensure < is checked

            kcp.ParseFastack(1, 0);
            Assert.That(kcp.snd_buf.Count, Is.EqualTo(0));
        }

        // test where no elements should be counted
        [Test]
        public void ParseFastAck_None()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // insert three segments into send buffer
            Segment one = new Segment{sn=2};
            kcp.snd_buf.Add(one);
            Segment two = new Segment{sn=3};
            kcp.snd_buf.Add(two);
            Segment three = new Segment{sn=4};
            kcp.snd_buf.Add(three);

            // sn needs to be between snd_una and snd_nxt
            kcp.snd_una = 1; // == sn to ensure <= is checked
            kcp.snd_nxt = 2; // sn + 1 to ensure < is checked

            // only segments with seg.sn < sn should have their fastack incremented
            // in this case, none
            kcp.ParseFastack(1, 0);
            Assert.That(kcp.snd_buf.Count, Is.EqualTo(3));
            Assert.That(kcp.snd_buf[0].fastack, Is.EqualTo(0));
            Assert.That(kcp.snd_buf[1].fastack, Is.EqualTo(0));
            Assert.That(kcp.snd_buf[2].fastack, Is.EqualTo(0));
        }

        // test where some elements should be counted
        [Test]
        public void ParseFastAck_Some()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // insert three segments into send buffer
            Segment one = new Segment{sn=2};
            kcp.snd_buf.Add(one);
            Segment two = new Segment{sn=3};
            kcp.snd_buf.Add(two);
            Segment three = new Segment{sn=4};
            kcp.snd_buf.Add(three);

            // sn needs to be between snd_una and snd_nxt
            kcp.snd_una = 3; // == sn to ensure <= is checked
            kcp.snd_nxt = 4; // sn + 1 to ensure < is checked

            // only segments with seg.sn < sn should have their fastack incremented
            kcp.ParseFastack(3, 0);
            Assert.That(kcp.snd_buf.Count, Is.EqualTo(3));
            Assert.That(kcp.snd_buf[0].fastack, Is.EqualTo(1));
            Assert.That(kcp.snd_buf[1].fastack, Is.EqualTo(0));
            Assert.That(kcp.snd_buf[2].fastack, Is.EqualTo(0));
        }

        // test where all elements should be counted
        [Test]
        public void ParseFastAck_All()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // insert three segments into send buffer
            Segment one = new Segment{sn=2};
            kcp.snd_buf.Add(one);
            Segment two = new Segment{sn=3};
            kcp.snd_buf.Add(two);
            Segment three = new Segment{sn=4};
            kcp.snd_buf.Add(three);

            // sn needs to be between snd_una and snd_nxt
            kcp.snd_una = 5; // == sn to ensure <= is checked
            kcp.snd_nxt = 6; // sn + 1 to ensure < is checked

            // only segments with seg.sn < sn should have their fastack incremented
            // in this case, all
            kcp.ParseFastack(5, 0);
            Assert.That(kcp.snd_buf.Count, Is.EqualTo(3));
            Assert.That(kcp.snd_buf[0].fastack, Is.EqualTo(1));
            Assert.That(kcp.snd_buf[1].fastack, Is.EqualTo(1));
            Assert.That(kcp.snd_buf[2].fastack, Is.EqualTo(1));
        }

        // peek without any messages in the receive queue
        [Test]
        public void PeekSize_Empty()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // peek should indicate empty size
            Assert.That(kcp.PeekSize(), Is.EqualTo(-1));
        }

        // peek with a complete unfragmented message in the receive queue
        [Test]
        public void PeekSize_Unfragmented()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // insert a small unfragmented message
            Segment seg = new Segment();
            seg.frg = 0; // frg == 0 indicates unfragmented message
            seg.data.Position = 42;
            kcp.rcv_queue.Enqueue(seg);

            // peek should get the unfragmented message's size
            Assert.That(kcp.PeekSize(), Is.EqualTo(42));
        }

        // peek with an incomplete fragmented message in the receive queue
        [Test]
        public void PeekSize_Fragmented_Incomplete()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // insert only one fragment of a two fragment message
            Segment part1 = new Segment();
            part1.frg = 1; // first segment's .frg indicates the last .frg number. total: 1,0
            part1.data.Position = 42; // set some data
            kcp.rcv_queue.Enqueue(part1);

            // peek should indicate incomplete fragments
            Assert.That(kcp.PeekSize(), Is.EqualTo(-1));
        }

        // peek with a complete fragmented message in the receive queue
        [Test]
        public void PeekSize_Fragmented_Complete()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // insert only one fragment of a two fragment message
            Segment part1 = new Segment();
            part1.frg = 1; // first segment's .frg indicates the last .frg number. total: 1,0
            part1.data.Position = 42; // set some data
            kcp.rcv_queue.Enqueue(part1);

            Segment part2 = new Segment();
            part2.frg = 0; // fragment count in reverse
            part2.data.Position = 11; // set some data
            kcp.rcv_queue.Enqueue(part2);

            // peek should peek through all fragments to calculate the whole message's size
            Assert.That(kcp.PeekSize(), Is.EqualTo(42 + 11));
        }

        [Test]
        public void WaitSnd()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // add some to send buffer and send buffer and queue
            kcp.snd_buf.Add(new Segment());
            kcp.snd_buf.Add(new Segment());
            kcp.snd_queue.Enqueue(new Segment());

            // WaitSnd should be send buffer + queue
            Assert.That(kcp.WaitSnd, Is.EqualTo(3));
        }

        [Test]
        public void WndUnused()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // add a few entries into receive queue
            kcp.rcv_queue.Enqueue(new Segment());
            kcp.rcv_queue.Enqueue(new Segment());
            kcp.rcv_queue.Enqueue(new Segment());

            // unused should be window size - queue size
            Assert.That(kcp.WndUnused(), Is.EqualTo(kcp.rcv_wnd - 3));
        }

        [Test]
        public void ShrinkBufFilledSendBuffer()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // add some to send buffer and send queue
            kcp.snd_buf.Add(new Segment{sn=2});
            kcp.snd_buf.Add(new Segment{sn=3});

            // ShrinkBuf should set snd_una to first send buffer element's 'sn'
            kcp.ShrinkBuf();
            Assert.That(kcp.snd_una, Is.EqualTo(2));
        }

        [Test]
        public void ShrinkBufEmptySendBuffer()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // ShrinkBuf with empty send buffer should set snd_una to snd_nxt
            kcp.snd_nxt = 42;
            kcp.ShrinkBuf();
            Assert.That(kcp.snd_una, Is.EqualTo(42));
        }

        [Test]
        public void SetMtu()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // set an allowed MTU that's smaller than default
            kcp.SetMtu(500);
            Assert.That(kcp.mtu, Is.EqualTo(500));
            Assert.That(kcp.buffer.Length, Is.GreaterThanOrEqualTo(500));

            // set an allowed MTU that's larger than default
            kcp.SetMtu(5000);
            Assert.That(kcp.mtu, Is.EqualTo(5000));
            Assert.That(kcp.buffer.Length, Is.GreaterThanOrEqualTo(5000));
        }

        [Test]
        public void SetNoDelay()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // enable nodelay with specific settings
            kcp.SetNoDelay(1, 11, 2, true);
            Assert.That(kcp.nodelay, Is.EqualTo(1));
            Assert.That(kcp.rx_minrto, Is.EqualTo(Kcp.RTO_NDL));
            Assert.That(kcp.interval, Is.EqualTo(11));
            Assert.That(kcp.fastresend, Is.EqualTo(2));
            Assert.That(kcp.nocwnd, Is.EqualTo(true));

            // disable nodelay with specific settings
            kcp.SetNoDelay(0, 22, 0, false);
            Assert.That(kcp.nodelay, Is.EqualTo(0));
            Assert.That(kcp.rx_minrto, Is.EqualTo(Kcp.RTO_MIN));
            Assert.That(kcp.interval, Is.EqualTo(22));
            Assert.That(kcp.fastresend, Is.EqualTo(0));
            Assert.That(kcp.nocwnd, Is.EqualTo(false));
        }

        [Test]
        public void SetIntervalTooSmall()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // setting a too small interval should limit it to 10
            kcp.SetInterval(0);
            Assert.That(kcp.interval, Is.EqualTo(10));
        }

        [Test]
        public void SetInterval()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // set interval
            kcp.SetInterval(2500);
            Assert.That(kcp.interval, Is.EqualTo(2500));
        }

        [Test]
        public void SetIntervalTooBig()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // setting a too big interval should limit it to 5000
            kcp.SetInterval(9999);
            Assert.That(kcp.interval, Is.EqualTo(5000));
        }

        [Test]
        public void SetWindowSize()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // set an allowed window size
            kcp.SetWindowSize(42, 512);
            Assert.That(kcp.snd_wnd, Is.EqualTo(42));
            Assert.That(kcp.rcv_wnd, Is.EqualTo(512));
        }

        [Test]
        public void SetWindowSizeWithTooSmallReceiveWindow()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // set window size with receive window < max fragment size (WND_RCV)
            kcp.SetWindowSize(42, Kcp.WND_RCV - 1);
            Assert.That(kcp.snd_wnd, Is.EqualTo(42));
            Assert.That(kcp.rcv_wnd, Is.EqualTo(Kcp.WND_RCV));
        }

        [Test]
        public void Check()
        {
            void Output(byte[] data, int len) {}

            // setup KCP
            Kcp kcp = new Kcp(0, Output);

            // update at time = 1
            kcp.Update(1);

            // check at time = 2
            uint next = kcp.Check(2);

            // check returns 'ts_flush + interval', or in other words,
            // 'interval' seconds after UPDATE was called. so 1+100 = 101.
            Assert.That(next, Is.EqualTo(1 + kcp.interval));
        }
    }
}