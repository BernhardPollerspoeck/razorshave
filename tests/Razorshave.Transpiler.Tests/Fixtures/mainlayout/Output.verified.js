export class MainLayout extends LayoutComponent {
  render() {
    return [
      h("div", { 'class': "page", 'b-ym0hq95m43': "" }, h("div", { 'class': "sidebar", 'b-ym0hq95m43': "" }, h(NavMenu, {})), markup("\r\n\r\n    "), h("main", { 'b-ym0hq95m43': "" }, markup("<div class=\"top-row px-4\" b-ym0hq95m43><a href=\"https://learn.microsoft.com/aspnet/core/\" target=\"_blank\" b-ym0hq95m43>About</a></div>\r\n\r\n        "), h("article", { 'class': "content px-4", 'b-ym0hq95m43': "" }, this.body))),
      markup("\r\n\r\n"),
      markup("<div id=\"blazor-error-ui\" data-nosnippet b-ym0hq95m43>\r\n    An unhandled error has occurred.\r\n    <a href=\".\" class=\"reload\" b-ym0hq95m43>Reload</a>\r\n    <span class=\"dismiss\" b-ym0hq95m43>🗙</span></div>")
    ];
  }
}
