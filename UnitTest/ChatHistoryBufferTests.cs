using Voxelgine.States;

namespace UnitTest;

public sealed class ChatHistoryBufferTests
{
	[Fact]
	public void KeepsNewestTwoHundredEntriesInOrder()
	{
		ChatHistoryBuffer history = new(200);
		for (int i = 0; i < 205; i++)
		{
			history.Add($"entry-{i}");
		}

		Assert.Equal(200, history.Count);
		Assert.Equal("entry-5", history.Entries[0]);
		Assert.Equal("entry-204", history.Entries[^1]);
	}

	[Fact]
	public void IgnoresEmptyEntriesAndClearsWithSession()
	{
		ChatHistoryBuffer history = new();
		history.Add("player: hello");
		history.Add(" ");
		Assert.Single(history.Entries);

		history.Clear();
		Assert.Empty(history.Entries);
		Assert.Equal(string.Empty, history.Text);
	}
}
