using BookStoreApi.Models;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BookStoreApi.Services;

public class BooksService(IOptions<BookStoreDatabaseSettings> bookStoreDatabaseSettings)
{
    private readonly IMongoCollection<Book> _booksCollection = new MongoClient(bookStoreDatabaseSettings.Value.ConnectionString)
        .GetDatabase(bookStoreDatabaseSettings.Value.DatabaseName)
        .GetCollection<Book>(bookStoreDatabaseSettings.Value.BooksCollectionName);

    public async Task<List<Book>> GetAsync() =>
        await _booksCollection.Find(_ => true).ToListAsync();

    public async Task<Book?> GetAsync(string id) =>
        await _booksCollection.Find(x => x.Id == id).FirstOrDefaultAsync();

    public async Task CreateAsync(Book newBook) =>
        await _booksCollection.InsertOneAsync(newBook);

    public async Task UpdateAsync(string id, Book updatedBook) =>
        await _booksCollection.ReplaceOneAsync(x => x.Id == id, updatedBook);

    public async Task RemoveAsync(string id) =>
        await _booksCollection.DeleteOneAsync(x => x.Id == id);

    public List<object> ValidateBooks(List<Book> books)
    {
        var invalidBooks = new List<object>();

        for (int i = 0; i < books.Count; i++)
        {
            var book = books[i];
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(book.BookName))
                errors.Add("Name is required.");
            if (string.IsNullOrWhiteSpace(book.Category))
                errors.Add("Category is required.");
            if (string.IsNullOrWhiteSpace(book.Author))
                errors.Add("Author is required.");
            if (book.Price <= 0)
                errors.Add("Price must be above 0.");

            if (errors.Count > 0)
                invalidBooks.Add(new { Index = i, Book = book, Errors = errors });
        }

        return invalidBooks;
    }

    public async Task<List<string>> CreateManyAsync(List<Book> books)
    {
        foreach (var book in books)
            book.Id = null;

        await _booksCollection.InsertManyAsync(books);
        return books.Select(b => b.Id!).ToList();
    }

    public async Task<List<Book>> SearchAsync(string? category, decimal? minPrice, decimal? maxPrice)
    {
        var builder = Builders<Book>.Filter;
        var filters = new List<FilterDefinition<Book>>();

        if (!string.IsNullOrWhiteSpace(category))
            filters.Add(builder.Eq(b => b.Category, category));

        if (minPrice.HasValue)
            filters.Add(builder.Gte(b => b.Price, minPrice.Value));

        if (maxPrice.HasValue)
            filters.Add(builder.Lte(b => b.Price, maxPrice.Value));

        var filter = filters.Count > 0 ? builder.And(filters) : builder.Empty;
        return await _booksCollection.Find(filter).ToListAsync();
    }

    public async Task<List<BookTopDto>> GetTopExpensiveAsync(int count) =>
        await _booksCollection
            .Find(_ => true)
            .SortByDescending(b => b.Price)
            .Limit(count)
            .Project(b => new BookTopDto(b.BookName, b.Price))
            .ToListAsync();

    public async Task<bool> UpdatePriceAsync(string id, decimal newPrice)
    {
        if (!ObjectId.TryParse(id, out _))
            return false;

        var update = Builders<Book>.Update.Set(b => b.Price, newPrice);
        var result = await _booksCollection.UpdateOneAsync(b => b.Id == id, update);
        return result.MatchedCount > 0;
    }
}

public record BookTopDto(string Name, decimal Price);
public record UpdatePriceRequest(decimal Price);