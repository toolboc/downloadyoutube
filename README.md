# downloadyoutube
Xamarin-Compatible Portable Class Library in C# for accessing MP4 and FLV steams from Youtube URLs based on [gantt/downloadyoutube](https://github.com/gantt/downloadyoutube)  

## Usage ##
        private async void init()
        {
            var service = new downloadyoutube.Service();
            var res = await service.GetFormats("https://www.youtube.com/watch?v=dQw4w9WgXcQ");
        }