namespace LazyList
{
    public class LazyListItemModel<T>
    {
        public T Value { get; private set; }
        public LazyListItemModel(T initialValue)
        {
            Value = initialValue;
        }
    }

}