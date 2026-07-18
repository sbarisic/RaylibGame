namespace Voxelgine.States;

internal sealed class ChatHistoryBuffer
{
	private readonly Queue<string> entries = new();

	public ChatHistoryBuffer(int capacity = 200)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
		Capacity = capacity;
	}

	public int Capacity { get; }

	public int Count => entries.Count;

	public IReadOnlyList<string> Entries => entries.ToArray();

	public string Text => string.Join(Environment.NewLine, entries);

	public void Add(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return;
		}

		entries.Enqueue(text);
		while (entries.Count > Capacity)
		{
			entries.Dequeue();
		}
	}

	public void Clear() => entries.Clear();
}
