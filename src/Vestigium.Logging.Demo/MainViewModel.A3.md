A3 Demo call-site patch (apply if MainViewModel.cs still uses the old Write signature)

Replace:
  VestigiumLog.Write(level, status, SelectedCategory, SelectedSubcategory, MessageText);
With:
  VestigiumLog.Write(EventIdFor(level), level, status, SelectedCategory, SelectedSubcategory, MessageText);

Replace both:
  VestigiumLog.Error(VestigiumStatus.Failed, "Network", "HTTP", "HttpIQ probe threw", ex);
With:
  VestigiumLog.Thrown(ex, VestigiumStatus.Failed, category: "Network", subcategory: "HTTP");

Prefix:
  Information(status  -> Information(1, status   (welcome: 5 and 11)
  Warning(status      -> Warning(2, status
  Write(VestigiumLogLevel.Information -> Write(1, VestigiumLogLevel.Information

Add:
  private static int EventIdFor(VestigiumLogLevel level) => level switch
  {
      VestigiumLogLevel.Verbose or VestigiumLogLevel.Debug => 0,
      VestigiumLogLevel.Information => 1,
      VestigiumLogLevel.Warning => 2,
      VestigiumLogLevel.Error => 3,
      VestigiumLogLevel.Fatal => 4,
      _ => 1
  };
