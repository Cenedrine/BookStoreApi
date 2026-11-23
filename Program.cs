using System.Text.Json;
using BookStoreApi.Models;
using BookStoreApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<BookStoreDatabaseSettings>(
    builder.Configuration.GetSection("BookStoreDatabase"));

builder.Services.AddSingleton<BooksService>();

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUi(options =>
    {
        options.DocumentPath = "/openapi/v1.json";
    });
}

app.UseHttpsRedirection();

var booksGroup = app.MapGroup("/books");

booksGroup.MapGet("/", async (BooksService booksService) =>
    await booksService.GetAsync());

booksGroup.MapGet("/{id:length(24)}", async (string id, BooksService booksService) =>
{
    var book = await booksService.GetAsync(id);
    return book is null ? Results.NotFound() : Results.Ok(book);
});

booksGroup.MapPost("/", async (Book newBook, BooksService booksService) =>
{
    await booksService.CreateAsync(newBook);
    return Results.Created($"/books/{newBook.Id}", newBook);
});

booksGroup.MapPut("/{id:length(24)}", async (string id, Book updatedBook, BooksService booksService) =>
{
    var existingBook = await booksService.GetAsync(id);
    if (existingBook is null) return Results.NotFound();

    updatedBook.Id = existingBook.Id;
    await booksService.UpdateAsync(id, updatedBook);
    return Results.NoContent();
});

booksGroup.MapDelete("/{id:length(24)}", async (string id, BooksService booksService) =>
{
    var book = await booksService.GetAsync(id);
    if (book is null) return Results.NotFound();

    await booksService.RemoveAsync(id);
    return Results.NoContent();
});

booksGroup.MapPost("/upload", async (IFormFile? file, BooksService booksService) =>
{
    if (file is null || file.Length == 0)
        return Results.BadRequest(new { Message = "No file uploaded." });

    if (!file.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { Message = "Only .json files are accepted." });

    List<Book>? books;
    try
    {
        using var stream = file.OpenReadStream();
        books = await JsonSerializer.DeserializeAsync<List<Book>>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }
    catch (JsonException ex)
    {
        return Results.BadRequest(new { Message = "Invalid JSON format.", Detail = ex.Message });
    }

    if (books is null || books.Count == 0)
        return Results.BadRequest(new { Message = "The JSON file must contain a non-empty array of books." });

    var invalidBooks = booksService.ValidateBooks(books);
    if (invalidBooks.Count > 0)
    {
        return Results.BadRequest(new
        {
            Message = "One or more books failed validation. Nothing was inserted.",
            InvalidBooks = invalidBooks
        });
    }

    var insertedIds = await booksService.CreateManyAsync(books);
    return Results.Ok(new
    {
        InsertedCount = insertedIds.Count,
        Ids = insertedIds
    });
})
.DisableAntiforgery();

booksGroup.MapGet("/search", async (
    string? category,
    decimal? minPrice,
    decimal? maxPrice,
    BooksService booksService) =>
{
    var results = await booksService.SearchAsync(category, minPrice, maxPrice);
    return Results.Ok(results);
});

booksGroup.MapGet("/top", async (int? count, BooksService booksService) =>
{
    int limit = count.GetValueOrDefault(3);
    if (limit <= 0)
        return Results.BadRequest(new { Message = "Count must be greater than 0." });

    var topBooks = await booksService.GetTopExpensiveAsync(limit);
    return Results.Ok(topBooks);
});

booksGroup.MapPatch("/{id}/price", async (
    string id,
    UpdatePriceRequest request,
    BooksService booksService) =>
{
    if (request.Price <= 0)
        return Results.BadRequest(new { Message = "Price must be above 0." });

    var updated = await booksService.UpdatePriceAsync(id, request.Price);
    return updated ? Results.NoContent() : Results.NotFound();
});

app.Run();