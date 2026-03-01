using System.Collections.Generic;

namespace IngameScript
{
    partial class Program
    {
        public class CircularBuffer<T>
        {
            private readonly Queue<T> _queue;
            private readonly int _capacity;

            public CircularBuffer(int capacity)
            {
                _capacity = capacity;
                _queue = new Queue<T>(capacity);
            }

            public void Enqueue(T item)
            {
                _queue.Enqueue(item);
                while (_queue.Count > _capacity)
                {
                    _queue.Dequeue();
                }
            }

            public T Dequeue() => _queue.Dequeue();
            public T Peek() => _queue.Peek();
            public int Count => _queue.Count;
            public void Clear() => _queue.Clear();

            public T[] ToArray() => _queue.ToArray();
        }
    }
}
