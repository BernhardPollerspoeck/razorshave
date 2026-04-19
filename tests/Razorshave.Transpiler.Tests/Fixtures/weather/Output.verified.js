export class Weather extends Component {
  forecasts = null;
  async onInitializedAsync() {
    await Task.delay(500);
    let startDate = DateOnly.fromDateTime(DateTime.now);
    let summaries = /* TODO: ImplicitArrayCreationExpression */ null;
    this.forecasts = Enumerable.range(1, 5).select(/* TODO: SimpleLambdaExpression */ null).toArray();
  }
}
