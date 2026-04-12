using File = TagLib.File;

namespace TaglibSharp.Tests.FileFormats
{
	[TestClass]
	public class AudibleFormatTest
	{
		static readonly string BaseDirectory = TestPath.Samples + "audible";

		[TestMethod]
		public void First ()
		{
			var tag = (TagLib.Audible.Tag)File.Create (Path.Combine (BaseDirectory, "first.aa")).Tag;
			Assert.AreEqual ("Glyn Hughes", tag.Album); // This is probably wrong. The publisher is not the album
			Assert.AreEqual ("Ricky Gervais, Steve Merchant, & Karl Pilkington", tag.Author);
			Assert.AreEqual ("&#169;2009 Ricky Gervais; (P)2009 Ricky Gervais", tag.Copyright);
			Assert.StartsWith ("This is the second in a new series of definitive discourses exploring the diversity of human", tag.Description);
			Assert.AreEqual ("Ricky Gervais, Steve Merchant, & Karl Pilkington", tag.Narrator);
			Assert.AreEqual ("The Ricky Gervais Guide to... NATURAL HISTORY (Unabridged)", tag.Title);
		}

		[TestMethod]
		[Ignore ("Not supported yet")]
		public void Second ()
		{
			var tag = (TagLib.Audible.Tag)File.Create (Path.Combine (BaseDirectory, "second.aax")).Tag;
			Assert.AreEqual ("Glyn Hughes", tag.Album); // This is probably wrong. The publisher is not the album
			Assert.AreEqual ("Ricky Gervais, Steve Merchant, & Karl Pilkington", tag.Author);
			Assert.AreEqual ("&#169;2009 Ricky Gervais; (P)2009 Ricky Gervais", tag.Copyright);
			Assert.StartsWith ("This is the second in a new series of definitive discourses exploring the diversity of human", tag.Description);
			Assert.AreEqual ("Ricky Gervais, Steve Merchant, & Karl Pilkington", tag.Narrator);
			Assert.AreEqual ("The Ricky Gervais Guide to... NATURAL HISTORY (Unabridged)", tag.Title);
		}

		[TestMethod]
		public void Third ()
		{
			var tag = (TagLib.Audible.Tag)File.Create (Path.Combine (BaseDirectory, "third.aa")).Tag;
			Assert.AreEqual ("Glyn Hughes", tag.Album); // This is probably wrong. The publisher is not the album
			Assert.AreEqual ("Ricky Gervais, Steve Merchant, & Karl Pilkington", tag.Author);
			Assert.AreEqual ("&#169;2009 Ricky Gervais; (P)2009 Ricky Gervais", tag.Copyright);
			Assert.StartsWith ("This is the second in a new series of definitive discourses exploring the diversity of human", tag.Description);
			Assert.AreEqual ("Ricky Gervais, Steve Merchant, & Karl Pilkington", tag.Narrator);
			Assert.AreEqual ("The Ricky Gervais Guide to... NATURAL HISTORY (Unabridged)", tag.Title);
		}

		[TestMethod]
		public void Fourth ()
		{
			var tag = (TagLib.Audible.Tag)File.Create (Path.Combine (BaseDirectory, "fourth.aa")).Tag;
			Assert.AreEqual ("Glyn Hughes", tag.Album); // This is probably wrong. The publisher is not the album
			Assert.AreEqual ("Ricky Gervais, Steve Merchant & Karl Pilkington", tag.Author);
			Assert.AreEqual ("&#169;2010 Ricky Gervais; (P)2010 Ricky Gervais", tag.Copyright);
			Assert.StartsWith ("The ninth episode in this new series considers the human body, its form, function, and failings", tag.Description);
			Assert.AreEqual ("Ricky Gervais, Steve Merchant & Karl Pilkington", tag.Narrator);
			Assert.AreEqual ("The Ricky Gervais Guide to... THE HUMAN BODY", tag.Title);
		}
	}
}
